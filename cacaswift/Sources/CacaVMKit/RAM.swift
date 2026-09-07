import Foundation

/// A byte-addressable memory block. Mirrors Caca.VM/VM/RAM.cs exactly,
/// including its one real invariant: `setByte` rejects any write at or below
/// `ramLimit` (the end of the loaded program image) with a clean error,
/// rather than letting a program overwrite its own code. `setSection` does
/// NOT go through that guard -- neither does the C# original (SoftwareInterrupts'
/// strcpy and KernelInterrupts' read-line both call it directly) -- which is
/// a real, open gap inherited here on purpose, not fixed silently: see
/// docs/architecture.md's note on read_int for where this already bit
/// cacalang's CacaVM backend.
public final class RandomAccessMemory {
    public private(set) var memory: [UInt8]

    /// Position of where the loaded program ends, exclusive. Writes at or
    /// below this address are rejected.
    public var ramLimit: Int = 0

    public init(size: Int) {
        memory = [UInt8](repeating: 0, count: size)
    }

    public func getByte(_ location: Int) -> UInt8 {
        memory[location]
    }

    /// Loads a program's bytes at address 0 — bypasses the RAMLimit guard
    /// deliberately: this is how the program itself gets there in the first
    /// place, before there is any "own code" yet to protect.
    func loadProgram(_ bytes: [UInt8]) {
        for (i, byte) in bytes.enumerated() {
            memory[i] = byte
        }
    }

    /// Throws `CacaVMError.protectedMemory` for a write at or below `ramLimit`
    /// -- matching RAM.cs's "attempted to overwrite its own code" guard.
    public func setByte(_ location: Int, _ value: UInt8) throws {
        guard location >= ramLimit else {
            throw CacaVMError.protectedMemory(location: location)
        }
        memory[location] = value
    }

    public func getSection(_ location: Int, _ length: Int) -> [UInt8] {
        Array(memory[location..<(location + length)])
    }

    /// No RAMLimit check -- matches RAM.cs's SetSection exactly, a
    /// deliberately-preserved gap, not an omission introduced here.
    public func setSection(_ location: Int, _ content: [UInt8]) {
        for (i, byte) in content.enumerated() {
            memory[location + i] = byte
        }
    }
}

public enum CacaVMError: Error, CustomStringConvertible {
    case protectedMemory(location: Int)
    case callStackOverflow(maxDepth: Int)
    case callStackUnderflow
    case stackUnderflow
    case divisionByZero(ip: Int)
    case invalidAddressMode(ip: Int, mode: Int)
    case unknownInterrupt(command: Int)
    case unknownSoftwareInterrupt(command: Int)

    public var description: String {
        switch self {
        case .protectedMemory(let location):
            return "The application attempted to overwrite its own code and was terminated. (address \(location))"
        case .callStackOverflow(let maxDepth):
            return "The application exceeded the maximum call stack depth (\(maxDepth) nested calls) and was terminated."
        case .callStackUnderflow:
            return "RET was executed with an empty call stack (no matching CLL/CLT/CLF)."
        case .stackUnderflow:
            return "POP was executed with an empty stack."
        case .divisionByZero(let ip):
            return "[CRITICAL ERROR] DIV at \(ip): division by zero."
        case .invalidAddressMode(let ip, let mode):
            return "[CRITICAL ERROR] Invalid address mode at \(ip) (\(mode))."
        case .unknownInterrupt(let command):
            return "Undocumented function: \(command)\nHalting for protection of data"
        case .unknownSoftwareInterrupt(let command):
            return "SWI 0x01: unknown mode 0x\(String(format: "%02X", command))\nHalting for protection of data"
        }
    }
}
