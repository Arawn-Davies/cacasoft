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
    private let console: LiveConsole

    @Published private(set) var halted = false
    @Published private(set) var steps = 0
    @Published private(set) var output: [OutputSegment] = []
    @Published var speed: Double = 3 // 1...5, matching DebugForm's TrackBar
    private(set) var isRunning = false

    private var previous: [String: Int32] = [:]
    private var runTask: Task<Void, Never>?

    init(code: [UInt8]) {
        self.code = code
        let console = LiveConsole { _ in } // set below, after self exists
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

    private func onHalted() {
        guard !halted else { return }
        halted = true
        isRunning = false
        runTask?.cancel()
        runTask = nil
        output.append(OutputSegment(text: "\n── Execution halted ─────────────────\n", kind: .info))
    }

    // MARK: - Run / pause / stop, mirrors StartRun/PauseRun/StopAndReset

    func run() {
        guard !isHalted else { return }
        isRunning = true
        let delays: [UInt64] = [0, 600_000_000, 150_000_000, 30_000_000, 8_000_000, 1_000_000]
        let delay = delays[max(1, min(5, Int(speed)))]
        runTask?.cancel()
        runTask = Task { [weak self] in
            while let self, self.isRunning, !self.isHalted {
                self.step()
                if delay > 0 {
                    try? await Task.sleep(nanoseconds: delay)
                } else {
                    await Task.yield()
                }
            }
        }
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
        case 0x20, 0x10, 0x11, 0x13, 0x14, 0x17, 0x18:
            operands = (mode == 0 || mode == 2) ? R(p1b) : "0x\(String(format: "%02X", UInt8(truncatingIfNeeded: p2)))"
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
