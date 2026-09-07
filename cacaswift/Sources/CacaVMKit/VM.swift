import Foundation

/// The register-based CIL virtual machine. A from-scratch Swift port of
/// Caca.VM/VM/VM.cs + VMextended.cs + func/op.cs, matching their exact
/// semantics -- including the fixes already applied there this session
/// (CallStack's off-by-one, the shared-address-space stack, register-
/// indirect MOM/MOE) and the standard library additions (signed 32-bit
/// print, strcmp, atoi). Every register is exactly as wide as the C#
/// original: AL/AH/BL/BH/CL/CH are bytes, A/B/C are their 16-bit composites,
/// X/Y/PC/IP/SP/SS are full Int32 -- narrower arithmetic here would silently
/// disagree with the reference implementation on wraparound.
public final class VM {
    // MARK: Registers — byte IDs, matching Spec-Architecture.md exactly

    public enum Register: UInt8 {
        case pc = 0xF0, ip = 0xF1, sp = 0xF2, ss = 0xF3
        case a = 0xF4, al = 0xF5, ah = 0xF6
        case b = 0xF7, bl = 0xF8, bh = 0xF9
        case c = 0xFA, cl = 0xFB, ch = 0xFC
        case x = 0xFD, y = 0xFE
    }

    public var pc: Int32 = 1
    public var ip: Int32 = 0
    /// Full-width: the stack lives in the shared RAM segment (Spec-Architecture.md
    /// §4), which can exceed 255 bytes — a byte-wide SP could never reach most of it.
    public var sp: Int32
    public var ss: Int32
    public var al: UInt8 = 0
    public var ah: UInt8 = 0
    public var bl: UInt8 = 0
    public var bh: UInt8 = 0
    public var cl: UInt8 = 0
    public var ch: UInt8 = 0
    public var x: Int32 = 0
    public var y: Int32 = 0

    /// Set by TEQ/TNE/TLT/TMT; read by JMT/JMF/CLT/CLF.
    var lastLogic: Bool = false

    public let ram: RandomAccessMemory
    let callStack = CallStack()
    public var running = false
    public var console: CacaConsole

    public init(program: [UInt8], ramSize: Int, console: CacaConsole = NullConsole()) {
        ram = RandomAccessMemory(size: ramSize)
        self.console = console
        ram.loadProgram(program)
        ram.ramLimit = program.count
        // SP/SS start at the top of the allocated RAM (an empty stack — the
        // next PSH decrements before writing), matching VM.cs's constructor.
        sp = Int32(ramSize - 1)
        ss = Int32(ramSize - 1)
    }

    // MARK: Register access — mirrors VMextended.cs's GetRegister/SetRegister

    func getRegister(_ id: UInt8) -> Int32 {
        guard let register = Register(rawValue: id) else { return 0 }
        switch register {
        case .pc: return pc
        case .ip: return ip
        case .sp: return sp
        case .ss: return ss
        case .a: return getSplit(al: al, ah: ah)
        case .al: return Int32(al)
        case .ah: return Int32(ah)
        case .b: return getSplit(al: bl, ah: bh)
        case .bl: return Int32(bl)
        case .bh: return Int32(bh)
        case .c: return getSplit(al: cl, ah: ch)
        case .cl: return Int32(cl)
        case .ch: return Int32(ch)
        case .x: return x
        case .y: return y
        }
    }

    /// Writing SP is silently ignored — SP is managed automatically by PSH/POP.
    func setRegister(_ id: UInt8, _ value: Int32) {
        guard let register = Register(rawValue: id) else { return }
        switch register {
        case .pc: pc = value
        case .ip: ip = value
        case .sp: break
        case .ss: ss = value
        case .a: (al, ah) = setSplit(value)
        case .al: al = UInt8(truncatingIfNeeded: value)
        case .ah: ah = UInt8(truncatingIfNeeded: value)
        case .b: (bl, bh) = setSplit(value)
        case .bl: bl = UInt8(truncatingIfNeeded: value)
        case .bh: bh = UInt8(truncatingIfNeeded: value)
        case .c: (cl, ch) = setSplit(value)
        case .cl: cl = UInt8(truncatingIfNeeded: value)
        case .ch: ch = UInt8(truncatingIfNeeded: value)
        case .x: x = value
        case .y: y = value
        }
    }

    private func getSplit(al: UInt8, ah: UInt8) -> Int32 {
        Int32(al) | (Int32(ah) << 8)
    }

    private func setSplit(_ value: Int32) -> (UInt8, UInt8) {
        (UInt8(truncatingIfNeeded: value), UInt8(truncatingIfNeeded: value >> 8))
    }

    // MARK: Execution

    /// Bits 47-42 of the 48-bit instruction (byte 0's upper 6 bits).
    private func opcode(_ firstByte: UInt8) -> UInt8 { (firstByte >> 2) & 0x3F }
    /// Bits 41-40 (byte 0's lower 2 bits).
    private func addressModeBits(_ firstByte: UInt8) -> UInt8 { firstByte & 0x03 }

    /// Reads param2 as a 32-bit little-endian value from bytes [offset, offset+4).
    func get32BitParameter(at offset: Int) -> Int32 {
        var value: UInt32 = 0
        for i in 0..<4 {
            value |= UInt32(ram.getByte(offset + i)) << (8 * i)
        }
        return Int32(bitPattern: value)
    }

    /// Runs until halted (KEI 0x02) or the program counter reaches a zero
    /// opcode byte, matching Execute()'s loop condition exactly.
    public func run() throws {
        running = true
        while running, Int(ip) < ram.memory.count, ram.memory[Int(ip)] != 0x00 {
            try tick()
        }
        running = false
    }

    /// Executes exactly one instruction — for a step debugger.
    public func tick() throws {
        guard Int(ip) < ram.memory.count, ram.memory[Int(ip)] != 0x00 else {
            running = false
            return
        }
        let firstByte = ram.memory[Int(ip)]
        let op = opcode(firstByte)
        if op == 0 {
            running = false
            return
        }
        let mode = AddressMode(rawValue: addressModeBits(firstByte)) ?? .regReg
        try execute(opcode: op, mode: mode)
        ip = pc
        pc += 1
    }
}

enum AddressMode: UInt8 {
    case regReg = 0, valReg = 1, regVal = 2, valVal = 3
}
