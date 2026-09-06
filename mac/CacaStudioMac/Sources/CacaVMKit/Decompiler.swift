import Foundation

/// Maps CacaVM opcode bytes to their mnemonic strings — a Swift port of
/// Caca.VM.Studio's Decompiler/Instruction.cs.
public enum DecompilerInstruction {
    public static func name(_ opcode: UInt8) -> String {
        switch opcode {
        case 0x01: return "MOV"
        case 0x3A: return "MOM"
        case 0x3B: return "MOE"
        case 0x02: return "SWP"
        case 0x1A: return "TEQ"
        case 0x1B: return "TNE"
        case 0x1C: return "TLT"
        case 0x1D: return "TMT"
        case 0x04: return "ADD"
        case 0x05: return "SUB"
        case 0x08: return "INC"
        case 0x09: return "DEC"
        case 0x30: return "MUL"
        case 0x31: return "DIV"
        case 0x06: return "SHL"
        case 0x07: return "SHR"
        case 0x0E: return "ROL"
        case 0x0F: return "ROR"
        case 0x0A: return "AND"
        case 0x0B: return "BOR"
        case 0x0C: return "XOR"
        case 0x0D: return "NOT"
        case 0x10: return "JMP"
        case 0x11: return "CLL"
        case 0x12: return "RET"
        case 0x13: return "JMT"
        case 0x14: return "JMF"
        case 0x17: return "CLT"
        case 0x18: return "CLF"
        case 0x20: return "PSH"
        case 0x21: return "POP"
        case 0x22: return "PSHN"
        case 0x23: return "POPN"
        case 0x24: return "INB"
        case 0x25: return "INW"
        case 0x26: return "IND"
        case 0x27: return "OUB"
        case 0x28: return "OUW"
        case 0x29: return "OUD"
        case 0x2A: return "SWI"
        case 0x2B: return "KEI"
        default: return ""
        }
    }
}

/// Maps CacaVM register identifier bytes back to their names — a Swift port
/// of Caca.VM.Studio's Decompiler/Registers.cs.
public enum DecompilerRegisters {
    public static func name(_ b: UInt8) -> String {
        switch b {
        case 0xF0: return "PC"
        case 0xF1: return "IP"
        case 0xF2: return "SP"
        case 0xF3: return "SS"
        case 0xF4: return "A"
        case 0xF5: return "AL"
        case 0xF6: return "AH"
        case 0xF7: return "B"
        case 0xF8: return "BL"
        case 0xF9: return "BH"
        case 0xFA: return "C"
        case 0xFB: return "CL"
        case 0xFC: return "CH"
        case 0xFD: return "X"
        case 0xFE: return "Y"
        default: return ""
        }
    }

    public static func isRegister(_ b: UInt8) -> Bool { b >= 0xF0 }
}

public enum DecompilerError: Error, CustomStringConvertible {
    case emptyExecutable

    public var description: String {
        switch self {
        case .emptyExecutable: return "Executable cannot be empty."
        }
    }
}

/// Decodes a CacaVM binary (raw bytecode or .ilc) back to assembly text — a
/// Swift port of Caca.VM.Studio's Decompiler/Decompiler.cs.
public enum Decompiler {
    public static func decompile(_ executable: [UInt8]) throws -> String {
        guard !executable.isEmpty else { throw DecompilerError.emptyExecutable }

        let code = IlcContainer.extractCode(executable)
        var lines = ["; Decompiled by Caca Studio", ""]

        var i = 0
        while i + 6 <= code.count {
            let b0 = code[i]
            let opcode = (b0 >> 2) & 0x3F
            let mode = AddressMode(rawValue: b0 & 0x03) ?? .regReg

            if opcode == 0x00 { break } // null opcode = end of program

            let p1b = code[i + 1]
            let p2b0 = code[i + 2]
            let p2 = Int32(bitPattern:
                UInt32(code[i + 2]) | (UInt32(code[i + 3]) << 8) |
                (UInt32(code[i + 4]) << 16) | (UInt32(code[i + 5]) << 24))

            let name = DecompilerInstruction.name(opcode)
            if name.isEmpty {
                lines.append("; unknown opcode 0x\(String(format: "%02X", opcode)) at offset \(i)")
                i += 6
                continue
            }

            lines.append(format(name: name, opcode: opcode, mode: mode, p1b: p1b, p2b0: p2b0, p2: p2))
            i += 6
        }

        return lines.joined(separator: "\n") + "\n"
    }

    private static func format(name: String, opcode: UInt8, mode: AddressMode, p1b: UInt8, p2b0: UInt8, p2: Int32) -> String {
        let reg = DecompilerRegisters.name
        switch opcode {
        case 0x12: // RET
            return name

        case 0x08, 0x09, 0x0D, 0x21: // INC DEC NOT POP
            return "\(name) \(reg(p1b))"

        case 0x20, 0x10, 0x11, 0x13, 0x14, 0x17, 0x18: // PSH JMP CLL JMT JMF CLT CLF
            if mode == .regReg || mode == .regVal {
                return "\(name) \(reg(p1b))"
            }
            return "\(name) 0x\(String(format: "%02X", UInt8(truncatingIfNeeded: p2)))"

        case 0x22, 0x23: // PSHN POPN — deliberately not the PSH/JMP bucket
            // above: a reservation count can exceed 255, so the literal-count
            // case must print the full 32-bit p2, not p2 truncated to a byte.
            if mode == .regReg || mode == .regVal {
                return "\(name) \(reg(p1b))"
            }
            return "\(name) 0x\(String(format: "%X", UInt32(bitPattern: p2)))"

        case 0x2A, 0x2B: // SWI KEI
            return "\(name) 0x\(String(format: "%02X", p1b))"

        case 0x02, 0x1A, 0x1B, 0x1C, 0x1D: // SWP TEQ TNE TLT TMT
            return "\(name) \(reg(p1b)), \(reg(p2b0))"

        case 0x06, 0x07, 0x0E, 0x0F: // SHL SHR ROL ROR
            return "\(name) \(reg(p1b)), \(p2)"

        case 0x3A: // MOM src, addr
            let src = DecompilerRegisters.isRegister(p1b) ? reg(p1b) : "0x\(String(format: "%02X", p1b))"
            return "\(name) \(src), 0x\(String(format: "%04X", UInt32(bitPattern: p2)))"

        case 0x3B: // MOE dest, addr
            return "\(name) \(reg(p1b)), 0x\(String(format: "%04X", UInt32(bitPattern: p2)))"

        case 0x24, 0x25, 0x26, 0x27, 0x28, 0x29: // INB INW IND OUB OUW OUD
            let regName = reg(p2b0)
            let p2str = regName.isEmpty ? "0x\(String(format: "%02X", p2b0))" : regName
            return "\(name) 0x\(String(format: "%02X", p1b)), \(p2str)"

        default: // MOV ADD SUB MUL DIV AND BOR XOR
            let p2str = mode == .regReg ? reg(p2b0) : "0x\(String(format: "%02X", UInt8(truncatingIfNeeded: p2)))"
            return "\(name) \(reg(p1b)), \(p2str)"
        }
    }
}

/// Reads/writes the minimal .ilc container format (spec §8) — a Swift port
/// of Caca.VM/Executable.cs's ExtractCode and MainForm.cs's WrapInIlc.
public enum IlcContainer {
    private static let magic: [UInt8] = [0x43, 0x49, 0x4C, 0x00] // "CIL\0"

    /// Wraps raw bytecode in a single-section .ilc container.
    public static func wrap(_ code: [UInt8]) -> [UInt8] {
        var bytes: [UInt8] = []
        bytes.append(contentsOf: magic)
        bytes.append(contentsOf: [0x02, 0x00])       // version 2 (LE)
        bytes.append(contentsOf: [0x01, 0x00])       // section count = 1 (LE)
        bytes.append(contentsOf: [0x01, 0x00])       // section type = 0x0001 code (LE)
        let length = UInt32(code.count)
        bytes.append(contentsOf: [
            UInt8(length & 0xFF), UInt8((length >> 8) & 0xFF),
            UInt8((length >> 16) & 0xFF), UInt8((length >> 24) & 0xFF),
        ])
        bytes.append(contentsOf: code)
        return bytes
    }

    /// Extracts the code section's payload for a .ilc file, or returns the
    /// buffer unchanged if it doesn't start with the magic header (raw
    /// bytecode).
    public static func extractCode(_ data: [UInt8]) -> [UInt8] {
        guard hasMagic(data) else { return data }

        let sectionCount = Int(data[6]) | (Int(data[7]) << 8)
        var offset = 8
        for _ in 0..<sectionCount {
            guard offset + 6 <= data.count else { break }
            let type = Int(data[offset]) | (Int(data[offset + 1]) << 8)
            let len = Int(data[offset + 2]) | (Int(data[offset + 3]) << 8) |
                (Int(data[offset + 4]) << 16) | (Int(data[offset + 5]) << 24)
            offset += 6
            if type == 0x0001 {
                guard offset + len <= data.count else { break }
                return Array(data[offset..<(offset + len)])
            }
            offset += len
        }
        return data
    }

    private static func hasMagic(_ data: [UInt8]) -> Bool {
        guard data.count >= 8 else { return false }
        return Array(data[0..<4]) == magic
    }
}
