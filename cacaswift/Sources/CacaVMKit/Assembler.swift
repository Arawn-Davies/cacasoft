import Foundation

public enum AssemblerError: Error, CustomStringConvertible {
    case emptySource
    case unknownMnemonic(String, line: Int)
    case invalidOperand(String, position: Int, line: Int)
    case emptyLabel(line: Int)
    case duplicateLabel(String, line: Int)
    case invalidDbByte(String, line: Int)

    public var description: String {
        switch self {
        case .emptySource: return "Source cannot be empty."
        case .unknownMnemonic(let m, let line): return "Unknown mnemonic '\(m)'. (line \(line))"
        case .invalidOperand(let t, let pos, let line):
            return "Invalid value for parameter \(pos): '\(t)'. (line \(line))"
        case .emptyLabel(let line): return "Empty label name. (line \(line))"
        case .duplicateLabel(let name, let line): return "Duplicate label '\(name)'. (line \(line))"
        case .invalidDbByte(let t, let line): return "DB: invalid byte value '\(t)'. (line \(line))"
        }
    }
}

/// Two-pass CIL assembler — a from-scratch port of Caca.VM.Studio's
/// Compiler.cs, matching its exact syntax: `;` or `//` comments, `label:` on
/// its own line, register names, decimal/hex integers, char literals,
/// label references, and `DB` for raw bytes (including string literals).
public enum Assembler {
    private static let mnemonics: [String: UInt8] = [
        "MOV": 0x01, "SWP": 0x02, "ADD": 0x04, "SUB": 0x05, "SHL": 0x06, "SHR": 0x07,
        "INC": 0x08, "DEC": 0x09, "AND": 0x0A, "BOR": 0x0B, "XOR": 0x0C, "NOT": 0x0D,
        "ROL": 0x0E, "ROR": 0x0F, "JMP": 0x10, "CLL": 0x11, "RET": 0x12, "JMT": 0x13,
        "JMF": 0x14, "CLT": 0x17, "CLF": 0x18, "TEQ": 0x1A, "TNE": 0x1B, "TLT": 0x1C,
        "TMT": 0x1D, "PSH": 0x20, "POP": 0x21, "PSHN": 0x22, "POPN": 0x23, "INB": 0x24, "INW": 0x25, "IND": 0x26,
        "OUB": 0x27, "OUW": 0x28, "OUD": 0x29, "SWI": 0x2A, "KEI": 0x2B, "MUL": 0x30,
        "DIV": 0x31, "MOM": 0x3A, "MOE": 0x3B,
    ]

    private static let registers: [String: UInt8] = [
        "PC": 0xF0, "IP": 0xF1, "SP": 0xF2, "SS": 0xF3,
        "A": 0xF4, "AL": 0xF5, "AH": 0xF6,
        "B": 0xF7, "BL": 0xF8, "BH": 0xF9,
        "C": 0xFA, "CL": 0xFB, "CH": 0xFC,
        "X": 0xFD, "Y": 0xFE,
    ]

    public static func assemble(_ source: String) throws -> [UInt8] {
        guard !source.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw AssemblerError.emptySource
        }

        let lines = source.replacingOccurrences(of: "\r", with: "").components(separatedBy: "\n")
        var labels: [String: Int] = [:]
        var byteOffset = 0

        // Pass 1: labels and byte offsets.
        for (i, rawLine) in lines.enumerated() {
            let stripped = strippedOfComment(rawLine).trimmingCharacters(in: .whitespaces)
            if stripped.isEmpty { continue }

            if stripped.hasSuffix(":") {
                let name = String(stripped.dropLast()).trimmingCharacters(in: .whitespaces)
                guard !name.isEmpty else { throw AssemblerError.emptyLabel(line: i + 1) }
                guard labels[name.uppercased()] == nil else {
                    throw AssemblerError.duplicateLabel(name, line: i + 1)
                }
                labels[name.uppercased()] = byteOffset
                continue
            }

            let tokens = tokenize(stripped)
            if tokens.isEmpty { continue }

            if tokens[0].uppercased() == "DB" {
                byteOffset += try countDbBytes(tokens, line: i + 1)
            } else {
                byteOffset += 6
            }
        }

        // Pass 2: emit.
        var bytes: [UInt8] = []
        bytes.reserveCapacity(byteOffset)
        for (i, rawLine) in lines.enumerated() {
            let stripped = strippedOfComment(rawLine).trimmingCharacters(in: .whitespaces)
            if stripped.isEmpty || stripped.hasSuffix(":") { continue }

            let tokens = tokenize(stripped)
            if tokens.isEmpty { continue }

            if tokens[0].uppercased() == "DB" {
                bytes.append(contentsOf: try assembleDb(tokens, line: i + 1))
            } else {
                bytes.append(contentsOf: try assembleLine(tokens, labels: labels, line: i + 1))
            }
        }

        return bytes
    }

    // MARK: Line assembly

    private static func assembleLine(_ tokens: [String], labels: [String: Int], line: Int) throws -> [UInt8] {
        let mnemonic = tokens[0].uppercased()
        guard let opcode = mnemonics[mnemonic] else {
            throw AssemblerError.unknownMnemonic(tokens[0], line: line)
        }

        var reg1 = false
        var reg2 = false
        var p1b: UInt8 = 0
        var p1i: Int32 = 0
        var p2i: Int32 = 0

        if tokens.count >= 2 {
            let (r, b, v) = try parseParam(tokens[1], labels: labels, line: line, isParam1: true)
            reg1 = r; p1b = b; p1i = v
        }

        if tokens.count >= 3 {
            let (r, _, v) = try parseParam(tokens[2], labels: labels, line: line, isParam1: false)
            reg2 = r; p2i = v
        } else if !reg1 {
            p2i = p1i
        }

        let mode: AddressMode
        switch (reg1, reg2) {
        case (true, true): mode = .regReg
        case (false, true): mode = .valReg
        case (true, false): mode = .regVal
        case (false, false): mode = .valVal
        }

        var instruction = [UInt8](repeating: 0, count: 6)
        instruction[0] = (opcode << 2) | mode.rawValue
        instruction[1] = p1b
        let bits = UInt32(bitPattern: p2i)
        instruction[2] = UInt8(truncatingIfNeeded: bits)
        instruction[3] = UInt8(truncatingIfNeeded: bits >> 8)
        instruction[4] = UInt8(truncatingIfNeeded: bits >> 16)
        instruction[5] = UInt8(truncatingIfNeeded: bits >> 24)
        return instruction
    }

    private static func parseParam(
        _ token: String, labels: [String: Int], line: Int, isParam1: Bool
    ) throws -> (isReg: Bool, byteVal: UInt8, intVal: Int32) {
        if let regByte = registers[token.uppercased()] {
            return (true, regByte, Int32(regByte))
        }

        if let charValue = parseCharLiteral(token) {
            return (false, charValue, Int32(charValue))
        }

        if let numVal = parseInteger(token) {
            let bv: UInt8 = isParam1 ? UInt8(truncatingIfNeeded: numVal) : 0
            return (false, bv, numVal)
        }

        if let addr = labels[token.uppercased()] {
            return (false, 0, Int32(addr))
        }

        throw AssemblerError.invalidOperand(token, position: isParam1 ? 1 : 2, line: line)
    }

    private static func parseInteger(_ token: String) -> Int32? {
        if token.lowercased().hasPrefix("0x") {
            guard let value = UInt32(token.dropFirst(2), radix: 16) else { return nil }
            return Int32(bitPattern: value)
        }
        return Int32(token)
    }

    private static func parseCharLiteral(_ token: String) -> UInt8? {
        guard token.count >= 3, token.hasPrefix("'"), token.hasSuffix("'") else { return nil }
        let inner = String(token.dropFirst().dropLast())
        let decoded = decodeEscapes(inner)
        guard decoded.count == 1, let scalar = decoded.first?.unicodeScalars.first else { return nil }
        return UInt8(scalar.value & 0xFF)
    }

    // MARK: DB

    private static func countDbBytes(_ tokens: [String], line: Int) throws -> Int {
        try assembleDb(tokens, line: line).count
    }

    private static func assembleDb(_ tokens: [String], line: Int) throws -> [UInt8] {
        var bytes: [UInt8] = []
        for token in tokens.dropFirst() {
            if token.hasPrefix("\"") && token.hasSuffix("\"") && token.count >= 2 {
                let inner = String(token.dropFirst().dropLast())
                for ch in decodeEscapes(inner).unicodeScalars {
                    bytes.append(UInt8(ch.value & 0xFF))
                }
            } else if let charValue = parseCharLiteral(token) {
                bytes.append(charValue)
            } else if let numVal = parseInteger(token) {
                bytes.append(UInt8(truncatingIfNeeded: numVal))
            } else {
                throw AssemblerError.invalidDbByte(token, line: line)
            }
        }
        return bytes
    }

    // MARK: Lexing

    /// Strips a `;` or `//` comment, respecting quotes — matches
    /// Compiler.cs's StripComment exactly.
    private static func strippedOfComment(_ line: String) -> String {
        var result = ""
        var inChar = false
        var inString = false
        let chars = Array(line)
        var i = 0
        while i < chars.count {
            let c = chars[i]
            if inString {
                result.append(c)
                if c == "\\", i + 1 < chars.count { i += 1; result.append(chars[i]) }
                else if c == "\"" { inString = false }
            } else if inChar {
                result.append(c)
                if c == "\\", i + 1 < chars.count { i += 1; result.append(chars[i]) }
                else if c == "'" { inChar = false }
            } else if c == "\"" {
                inString = true; result.append(c)
            } else if c == "'" {
                inChar = true; result.append(c)
            } else if c == ";" {
                break
            } else if c == "/", i + 1 < chars.count, chars[i + 1] == "/" {
                break
            } else {
                result.append(c)
            }
            i += 1
        }
        return result
    }

    /// Splits on space/tab/comma, keeping quoted char/string literals intact.
    private static func tokenize(_ line: String) -> [String] {
        var tokens: [String] = []
        var current = ""
        var inChar = false
        var inString = false
        let chars = Array(line)
        var i = 0
        while i < chars.count {
            let c = chars[i]
            if inString {
                current.append(c)
                if c == "\\", i + 1 < chars.count { i += 1; current.append(chars[i]) }
                else if c == "\"" { inString = false }
            } else if inChar {
                current.append(c)
                if c == "\\", i + 1 < chars.count { i += 1; current.append(chars[i]) }
                else if c == "'" { inChar = false }
            } else if c == "\"" {
                inString = true; current.append(c)
            } else if c == "'" {
                inChar = true; current.append(c)
            } else if c == " " || c == "\t" || c == "," {
                if !current.isEmpty { tokens.append(current); current = "" }
            } else {
                current.append(c)
            }
            i += 1
        }
        if !current.isEmpty { tokens.append(current) }
        return tokens
    }

    /// Backslash escapes inside a char or string literal: \n \t \r \0 \\ \' \".
    private static func decodeEscapes(_ text: String) -> String {
        var result = ""
        var chars = Array(text)[...]
        while let c = chars.first {
            chars = chars.dropFirst()
            if c == "\\", let next = chars.first {
                chars = chars.dropFirst()
                switch next {
                case "n": result.append("\n")
                case "t": result.append("\t")
                case "r": result.append("\r")
                case "0": result.append("\0")
                default: result.append(next)
                }
            } else {
                result.append(c)
            }
        }
        return result
    }
}
