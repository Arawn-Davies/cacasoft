/// The VM's I/O boundary — mirrors Caca.VM.Handlers.VConsole. Implement this
/// to redirect output to a SwiftUI view, a test buffer, or (via
/// `StandardConsole`) the real terminal.
public protocol CacaConsole: AnyObject {
    func write(_ text: String)
    func writeLine(_ text: String)
    /// Reads a single byte from input. `NullConsole` throws; a real console
    /// blocks for one character.
    func read() throws -> UInt8
    /// Reads a full line (without the terminator). `NullConsole` throws.
    func readLine() throws -> String
}

public final class NullConsole: CacaConsole {
    public init() {}
    public func write(_ text: String) {}
    public func writeLine(_ text: String) {}
    public func read() throws -> UInt8 { throw CacaVMError.unknownInterrupt(command: -1) }
    public func readLine() throws -> String { throw CacaVMError.unknownInterrupt(command: -1) }
}

/// Buffers everything written, for tests — the Swift equivalent of the C#
/// suite's TestConsole.
public final class BufferConsole: CacaConsole {
    public private(set) var output = ""
    private var input: [String]
    private var byteInput: [UInt8]

    public init(input: [String] = [], byteInput: [UInt8] = []) {
        self.input = input
        self.byteInput = byteInput
    }

    public func write(_ text: String) { output += text }
    public func writeLine(_ text: String) { output += text + "\n" }

    public func read() throws -> UInt8 {
        guard !byteInput.isEmpty else { throw CacaVMError.unknownInterrupt(command: -1) }
        return byteInput.removeFirst()
    }

    public func readLine() throws -> String {
        guard !input.isEmpty else { throw CacaVMError.unknownInterrupt(command: -1) }
        return input.removeFirst()
    }
}
