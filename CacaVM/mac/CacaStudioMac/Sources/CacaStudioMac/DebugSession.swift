import AppKit
import CacaVMKit

/// One register/stack snapshot row, computed fresh after every step —
/// mirrors DebugForm.cs's UpdateRegisters/UpdateMemory, minus the WinForms
/// RichTextBox plumbing.
struct RegisterRow: Identifiable {
    let id: String
    let name: String
    let value: String
    let changed: Bool
}

struct StackByte: Identifiable {
    let id: Int
    let address: Int
    let value: UInt8
}

struct MemoryRow: Identifiable {
    let id: Int
    let address: Int
    let bytes: [UInt8]
    let decoded: String
    let isCurrent: Bool
}

/// A step-through debug session around one VM instance — a Swift port of
/// Caca.VM.Studio's DebugForm, driving the same VM.tick()/CacaVMKit that
/// backs Compile & Run, just one instruction at a time.
@MainActor
final class DebugSession: ObservableObject {
    let code: [UInt8]
    private(set) var vm: VM
    private let console: DebugConsole

    /// The original cacalang source, only when this session was opened from
    /// cacalang (not raw CIL) — see AppState.resolveToCIL(). The debug
    /// window shows a source pane alongside the instruction list exactly
    /// when this is non-nil.
    let cacalangSource: String?

    /// (byte offset, 1-based source line) pairs, ascending by offset — see
    /// CilEmitter.Emit's lineMap and CacalangCompiler.Result.lineMap. Empty
    /// when cacalangSource is nil, or if it came from an older cacalang CLI
    /// built before the sidecar existed.
    private let lineMap: [(byteOffset: Int, sourceLine: Int)]

    @Published private(set) var halted = false
    @Published private(set) var steps = 0
    @Published private(set) var output: [OutputSegment] = []
    @Published var speed: Double = 3 // 1...5, matching DebugForm's TrackBar
    // @Published, not just private(set): the Run/Pause toolbar button reads
    // this directly, and without it the label only happens to update
    // because `steps` (a sibling @Published property) also changes on every
    // tick and forces a re-render anyway — true today, but fragile, and
    // would silently stop working the moment run()/pause() didn't happen to
    // be followed by a steps mutation.
    @Published private(set) var isRunning = false

    private var previous: [String: Int32] = [:]
    private var runTask: Task<Void, Never>?

    init(code: [UInt8], cacalangSource: String? = nil, lineMap: [(byteOffset: Int, sourceLine: Int)] = []) {
        self.code = code
        self.cacalangSource = cacalangSource
        self.lineMap = lineMap
        let console = DebugConsole()
        self.console = console
        vm = VM(program: code, ramSize: Globals.defaultRamSize, console: console)
        vm.running = true
        console.onOutput = { [weak self] text in
            guard let self else { return }
            self.output.append(OutputSegment(text: text))
        }
        snapshot()
    }

    var isHalted: Bool {
        halted || !vm.running || Int(vm.ip) >= vm.ram.memory.count || vm.ram.memory[Int(vm.ip)] == 0x00
    }

    /// The cacalang source line behind the instruction at `vm.ip` right now
    /// — the LAST lineMap entry whose byte offset is <= ip, since entries
    /// are recorded at each statement's own starting offset and a running
    /// PC sits somewhere at or after the start of whichever statement is
    /// currently executing. nil when there's no map (a raw-CIL session) or
    /// ip is before the first recorded statement (inside the compiler's own
    /// generated prologue/string-data section, which has no source line).
    var currentSourceLine: Int? {
        let ip = Int(vm.ip)
        var result: Int?
        for entry in lineMap {
            guard entry.byteOffset <= ip else { break }
            result = entry.sourceLine
        }
        return result
    }

    // MARK: - Step logic, mirrors DoStep/OnHalted

    func step() {
        guard !isHalted else { onHalted(); return }
        snapshot()
        do {
            try vm.tick()
        } catch {
            output.append(OutputSegment(text: "\n✗ Runtime exception: \(error)\n", kind: .error))
            halted = true
        }
        steps += 1
        if isHalted { onHalted() }
    }

    /// Guards onHalted()'s one-time side effects (stopping the run loop,
    /// appending the halt message) — deliberately NOT the same flag as
    /// `halted` itself: both step() and tickSilently()'s catch blocks set
    /// `halted = true` directly before calling onHalted(), so gating on
    /// `halted` made the guard true from the very first call, silently
    /// skipping isRunning = false (and everything else) every single time —
    /// the Run/Pause button stayed stuck on "Pause" after any runtime error.
    private var didAnnounceHalt = false

    private func onHalted() {
        guard !didAnnounceHalt else { return }
        didAnnounceHalt = true
        halted = true
        isRunning = false
        runTask?.cancel()
        runTask = nil
        output.append(OutputSegment(text: "\n── Execution halted ─────────────────\n", kind: .info))
    }

    // MARK: - Run / pause / stop, mirrors StartRun/PauseRun/StopAndReset

    /// Speed 6 ("Max") runs flat-out: no per-tick delay, and — just as
    /// important — no per-tick UI refresh either. A program like FizzBuzz
    /// reserves ~270 bytes of locals, which cacalang's compiler currently
    /// zero-initializes with one PSH 0 per byte (a real ISA constraint: SP
    /// only ever moves via single-byte PSH/POP, see CilEmitter.cs and
    /// VM.swift's setRegister — there is no faster way to reserve N bytes of
    /// stack). Re-rendering the full registers/stack/memory panel on every
    /// one of those ticks adds real, separate overhead on top of that: at
    /// the old fixed 1ms-minimum delay, each visible "step" during Run
    /// actually took ~40ms wall-clock in practice (measured directly: 3
    /// real seconds of Run at the default speed produced only 75 steps, not
    /// the ~100 the nominal delay implied) — almost all of it SwiftUI
    /// re-rendering hundreds of Text rows, not VM execution. Ticking silently
    /// in a batch and publishing @Published state only periodically fixes
    /// both at once.
    static let maxSpeed: Double = 6

    private static let delays: [UInt64] = [0, 600_000_000, 150_000_000, 30_000_000, 8_000_000, 1_000_000, 0]

    func run() {
        guard !isHalted else { return }
        isRunning = true
        runTask?.cancel()
        runTask = Task { [weak self] in
            var ticksSinceRefresh = 0
            while let self, self.isRunning, !self.isHalted {
                // Read speed fresh every iteration — dragging the slider
                // mid-run must take effect immediately, not just on the
                // next press of Run. Capturing it once outside this loop
                // (the original bug) meant the slider only ever mattered
                // for whichever speed was selected at the moment Run was
                // clicked.
                let speedIndex = max(1, min(6, Int(self.speed)))
                let isMaxSpeed = speedIndex == 6
                if isMaxSpeed {
                    self.tickSilently()
                    ticksSinceRefresh += 1
                    // Refresh the UI roughly 30 times/sec instead of every
                    // tick — plenty to watch it run, far cheaper to draw.
                    if ticksSinceRefresh >= 200 {
                        self.publishAfterSilentTicks()
                        ticksSinceRefresh = 0
                        await Task.yield()
                    }
                } else {
                    if ticksSinceRefresh > 0 {
                        self.publishAfterSilentTicks()
                        ticksSinceRefresh = 0
                    }
                    self.step()
                    let delay = Self.delays[speedIndex]
                    if delay > 0 { try? await Task.sleep(nanoseconds: delay) }
                }
            }
            if ticksSinceRefresh > 0 {
                self?.publishAfterSilentTicks()
            }
        }
    }

    /// Executes one tick without touching any @Published property — used
    /// only by the max-speed Run loop, which batches UI refreshes instead
    /// of triggering one on every single tick. Mirrors step()'s logic
    /// exactly, minus the parts that would otherwise force a SwiftUI
    /// re-render 200 times more often than necessary.
    private func tickSilently() {
        guard !isHalted else { return }
        do {
            try vm.tick()
        } catch {
            pendingSilentError = "\n✗ Runtime exception: \(error)\n"
            halted = true
        }
        stepsSinceLastPublish += 1
    }

    private var stepsSinceLastPublish = 0
    private var pendingSilentError: String?

    /// Flushes the batched step count and any pending error into the
    /// @Published properties the UI actually observes — the one point in
    /// the max-speed loop where a re-render is allowed to happen.
    private func publishAfterSilentTicks() {
        snapshot()
        steps += stepsSinceLastPublish
        stepsSinceLastPublish = 0
        if let pendingSilentError {
            output.append(OutputSegment(text: pendingSilentError, kind: .error))
            self.pendingSilentError = nil
        }
        if isHalted { onHalted() }
    }

    func pause() {
        isRunning = false
        runTask?.cancel()
        runTask = nil
    }

    func stopAndReset() {
        pause()
        vm = VM(program: code, ramSize: Globals.defaultRamSize, console: console)
        vm.running = true
        halted = false
        didAnnounceHalt = false
        steps = 0
        output.removeAll()
        snapshot()
    }

    private func snapshot() {
        previous = [
            "PC": vm.pc, "IP": vm.ip, "SP": vm.sp, "SS": vm.ss,
            "AL": Int32(vm.al), "AH": Int32(vm.ah),
            "BL": Int32(vm.bl), "BH": Int32(vm.bh),
            "CL": Int32(vm.cl), "CH": Int32(vm.ch),
            "X": vm.x, "Y": vm.y,
        ]
    }

    private func changed(_ name: String, _ value: Int32) -> Bool { previous[name] != value }

    // MARK: - Register / stack display, mirrors UpdateRegisters

    var registerRows: [[RegisterRow]] {
        func row(_ name: String, _ value: Int32, width: Int = 2) -> RegisterRow {
            RegisterRow(id: name, name: name, value: "0x" + String(format: "%0\(width)X", value), changed: changed(name, value))
        }
        let a = Int32(vm.ah) << 8 | Int32(vm.al)
        let b = Int32(vm.bh) << 8 | Int32(vm.bl)
        let c = Int32(vm.ch) << 8 | Int32(vm.cl)
        return [
            [row("PC", vm.pc), row("IP", vm.ip)],
            [row("SP", vm.sp), row("SS", vm.ss)],
            [row("AL", Int32(vm.al)), row("AH", Int32(vm.ah))],
            [row("BL", Int32(vm.bl)), row("BH", Int32(vm.bh))],
            [row("CL", Int32(vm.cl)), row("CH", Int32(vm.ch))],
            [RegisterRow(id: "X", name: "X", value: "0x" + String(format: "%08X", vm.x), changed: changed("X", vm.x))],
            [RegisterRow(id: "Y", name: "Y", value: "0x" + String(format: "%08X", vm.y), changed: changed("Y", vm.y))],
            [RegisterRow(id: "A", name: "A", value: "0x" + String(format: "%04X", a), changed: false)],
            [RegisterRow(id: "B", name: "B", value: "0x" + String(format: "%04X", b), changed: false)],
            [RegisterRow(id: "C", name: "C", value: "0x" + String(format: "%04X", c), changed: false)],
        ]
    }

    var stackBytes: [StackByte] {
        let sp = Int(vm.sp)
        let top = vm.ram.memory.count - 1
        guard sp < top else { return [] }
        return (sp...top).map { StackByte(id: $0, address: $0, value: vm.ram.getByte($0)) }
    }

    // MARK: - Memory / code view, mirrors UpdateMemory/DecodeInstruction

    var memoryRows: [MemoryRow] {
        let codeEnd = vm.ram.ramLimit
        let ip = Int(vm.ip)
        var rows: [MemoryRow] = []
        var i = 0
        while i + 6 <= codeEnd {
            let bytes = Array(vm.ram.memory[i..<(i + 6)])
            rows.append(MemoryRow(id: i, address: i, bytes: bytes, decoded: decode(at: i), isCurrent: i == ip))
            i += 6
        }
        return rows
    }

    private func decode(at offset: Int) -> String {
        let mem = vm.ram.memory
        guard offset + 6 <= mem.count else { return "" }
        let b0 = mem[offset]
        if b0 == 0 { return "(end)" }
        let opcode = (b0 >> 2) & 0x3F
        let mode = b0 & 0x03
        let p1b = mem[offset + 1]
        let p2b0 = mem[offset + 2]
        let p2 = Int32(bitPattern:
            UInt32(mem[offset + 2]) | (UInt32(mem[offset + 3]) << 8) |
            (UInt32(mem[offset + 4]) << 16) | (UInt32(mem[offset + 5]) << 24))

        let name = DecompilerInstruction.name(opcode)
        guard !name.isEmpty else { return "??? (0x\(String(format: "%02X", opcode)))" }

        func R(_ b: UInt8) -> String { DecompilerRegisters.name(b) }
        func Rx(_ b: UInt8) -> String { DecompilerRegisters.isRegister(b) ? R(b) : "0x\(String(format: "%02X", b))" }

        let operands: String
        switch opcode {
        case 0x12: operands = ""
        case 0x08, 0x09, 0x0D, 0x21: operands = R(p1b)
        case 0x2A, 0x2B: operands = "0x\(String(format: "%02X", p1b))"
        case 0x02, 0x1A, 0x1B, 0x1C, 0x1D: operands = "\(R(p1b)), \(R(p2b0))"
        case 0x06, 0x07, 0x0E, 0x0F: operands = "\(R(p1b)), \(p2)"
        case 0x3A: operands = "\(Rx(p1b)), 0x\(String(format: "%04X", UInt32(bitPattern: p2)))"
        case 0x3B: operands = "\(R(p1b)), 0x\(String(format: "%04X", UInt32(bitPattern: p2)))"
        case 0x20: // PSH — pushes exactly one byte, so truncating p2 is correct
            operands = (mode == 0 || mode == 2) ? R(p1b) : "0x\(String(format: "%02X", UInt8(truncatingIfNeeded: p2)))"
        case 0x10, 0x11, 0x13, 0x14, 0x17, 0x18: // JMP CLL JMT JMF CLT CLF — full address, not a byte
            operands = (mode == 0 || mode == 2) ? R(p1b) : "0x\(String(format: "%04X", UInt32(bitPattern: p2)))"
        case 0x22, 0x23: // PSHN POPN — not the PSH case above: counts can exceed 255
            operands = (mode == 0 || mode == 2) ? R(p1b) : "0x\(String(format: "%X", UInt32(bitPattern: p2)))"
        default:
            operands = mode == 0 ? "\(R(p1b)), \(R(p2b0))" : "\(R(p1b)), 0x\(String(format: "%02X", UInt8(truncatingIfNeeded: p2)))"
        }
        return operands.isEmpty ? name : "\(name) \(operands)"
    }

    var currentInstructionText: String {
        isHalted ? "" : "▶  0x\(String(format: "%02X", vm.ip))  \(decode(at: Int(vm.ip)))"
    }
}

/// Matches Globals.DefaultRamSize (1,048,576 bytes / 1 MiB) — CacaVMKit
/// doesn't expose the C# Globals type, so the constant is restated here.
enum Globals {
    static let defaultRamSize = 1_048_576
}

/// DebugSession's console — deliberately NOT LiveConsole. vm.tick() runs
/// synchronously on the main actor here (that's what makes single-stepping
/// simple), so blocking on a semaphore for input, as LiveConsole now does
/// for Compile & Run's background-thread execution, would freeze the main
/// thread waiting for input only the main thread could ever supply — a
/// guaranteed deadlock the instant a stepped/run program hit a read.
/// Throwing immediately (the original, safe behaviour) is a real, known
/// gap — interactive input while single-stepping isn't supported — but a
/// gap is recoverable; a frozen app is not.
private final class DebugConsole: CacaConsole {
    var onOutput: (String) -> Void = { _ in }
    func write(_ text: String) { onOutput(text) }
    func writeLine(_ text: String) { onOutput(text + "\n") }
    func read() throws -> UInt8 { throw DebugConsoleError.inputNotSupported }
    func readLine() throws -> String { throw DebugConsoleError.inputNotSupported }
}

/// A dedicated error rather than reusing CacaVMError.unknownInterrupt (the
/// generic "no such interrupt" sentinel NullConsole also throws for reads):
/// that description reads as a real VM-level fault ("Undocumented function:
/// -1 / Halting for protection of data"), which is actively misleading here
/// — this isn't a VM error at all, it's a known, deliberate IDE limitation
/// with a clear cause and a clear workaround.
private enum DebugConsoleError: Error, CustomStringConvertible {
    case inputNotSupported

    var description: String {
        "this program is waiting for input, which the debugger doesn't support yet — use Compile & Run instead, which does."
    }
}
