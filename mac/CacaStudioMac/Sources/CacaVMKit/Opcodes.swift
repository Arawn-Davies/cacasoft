import Foundation

/// Instruction execution — a from-scratch port of func/op.cs's ParseOpcode,
/// opcode by opcode, matching its exact behavior including the two axes
/// that differ between instructions and are easy to get backwards:
///
/// - Which operand is "the register": for ADD/SUB/MOV/AND/BOR/XOR/MUL, param1
///   (dest) is always a register and only param2's mode bit matters (Reg or
///   Val). For a single-operand instruction (JMP/CLL/JMT/JMF/CLT/CLF/PSH/
///   INC/DEC/NOT), only param1's mode bit is meaningful; the assembler
///   always encodes a single register operand as RegVal (param2 is simply
///   never populated), never RegReg.
/// - TEQ/TNE/TLT/TMT ALWAYS read both operands as register IDs, regardless
///   of the encoded address mode — op.cs never inspects the mode for them.
///
/// MOM/MOE's register-indirect addressing (RegReg/ValReg meaning "the
/// address is the register's runtime value, not a literal") is this
/// session's own fix to the C# original, ported here already fixed.
extension VM {
    func execute(opcode: UInt8, mode: AddressMode) throws {
        switch opcode {
        case 0x01: try opMov(mode)
        case 0x02: opSwp()
        case 0x04: opAdd(mode)
        case 0x05: opSub(mode)
        case 0x06: opShl()
        case 0x07: opShr()
        case 0x08: opInc()
        case 0x09: opDec()
        case 0x0A: opAnd(mode)
        case 0x0B: opBor(mode)
        case 0x0C: opXor(mode)
        case 0x0D: opNot()
        case 0x0E: opRol()
        case 0x0F: opRor()
        case 0x10: opJmp(mode)
        case 0x11: try opCll(mode)
        case 0x12: try opRet()
        case 0x13: opJmt(mode)
        case 0x14: opJmf(mode)
        case 0x17: try opClt(mode)
        case 0x18: try opClf(mode)
        case 0x1A: opTeq()
        case 0x1B: opTne()
        case 0x1C: opTlt()
        case 0x1D: opTmt()
        case 0x20: try opPsh(mode)
        case 0x21: try opPop()
        case 0x24, 0x25, 0x26, 0x27, 0x28, 0x29:
            // INB/INW/IND/OUB/OUW/OUD — reserved, no-ops in the reference VM
            // too (see wiki/Spec-Instructions.md §6.6's own notice). PC += 5
            // and nothing else, matching that exactly, not "improving" on it.
            pc += 5
        case 0x2A: try opSwi()
        case 0x2B: try opKei()
        case 0x30: opMul(mode)
        case 0x31: try opDiv(mode)
        case 0x3A: try opMom(mode)
        case 0x3B: try opMoe(mode)
        default:
            pc += 5
        }
    }

    // MARK: Register/memory

    private func opMov(_ mode: AddressMode) throws {
        let dest = ram.memory[Int(ip) + 1]
        // dest (param1) is always a register for MOV, so mode is always
        // RegReg (src is a register) or RegVal (src is a literal) — never
        // ValReg/ValVal. Checking regReg, not regVal, to match binarySource's
        // structure (both correct; getting this backwards was a real bug
        // caught only by actually running MOV X, <literal> and finding X
        // stayed 0 — see this file's own history).
        let value: Int32 = mode == .regReg
            ? getRegister(ram.memory[Int(ip) + 2])
            : get32BitParameter(at: Int(ip) + 2)
        setRegister(dest, value)
        pc += 5
    }

    private func opSwp() {
        let r1 = ram.memory[Int(ip) + 1]
        let r2 = ram.memory[Int(ip) + 2]
        let tmp = getRegister(r1)
        setRegister(r1, getRegister(r2))
        setRegister(r2, tmp)
        pc += 5
    }

    // MARK: Arithmetic

    private func binarySource(_ mode: AddressMode) -> Int32 {
        mode == .regReg
            ? getRegister(ram.memory[Int(ip) + 2])
            : get32BitParameter(at: Int(ip) + 2)
    }

    private func opAdd(_ mode: AddressMode) {
        let dest = ram.memory[Int(ip) + 1]
        setRegister(dest, getRegister(dest) &+ binarySource(mode))
        pc += 5
    }

    private func opSub(_ mode: AddressMode) {
        let dest = ram.memory[Int(ip) + 1]
        setRegister(dest, getRegister(dest) &- binarySource(mode))
        pc += 5
    }

    private func opMul(_ mode: AddressMode) {
        let dest = ram.memory[Int(ip) + 1]
        setRegister(dest, getRegister(dest) &* binarySource(mode))
        pc += 5
    }

    private func opDiv(_ mode: AddressMode) throws {
        let dest = ram.memory[Int(ip) + 1]
        let divisor = binarySource(mode)
        guard divisor != 0 else { throw CacaVMError.divisionByZero(ip: Int(ip)) }
        setRegister(dest, getRegister(dest) / divisor)
        pc += 5
    }

    private func opInc() {
        let reg = ram.memory[Int(ip) + 1]
        setRegister(reg, getRegister(reg) &+ 1)
        pc += 5
    }

    private func opDec() {
        let reg = ram.memory[Int(ip) + 1]
        setRegister(reg, getRegister(reg) &- 1)
        pc += 5
    }

    // MARK: Bitwise

    private func shiftAmount() -> Int32 { get32BitParameter(at: Int(ip) + 2) & 31 }

    private func opShl() {
        let src = ram.memory[Int(ip) + 1]
        setRegister(src, getRegister(src) << shiftAmount())
        pc += 5
    }

    private func opShr() {
        let src = ram.memory[Int(ip) + 1]
        let value = UInt32(bitPattern: getRegister(src))
        setRegister(src, Int32(bitPattern: value >> UInt32(shiftAmount())))
        pc += 5
    }

    private func opRol() {
        let src = ram.memory[Int(ip) + 1]
        let n = UInt32(shiftAmount())
        let value = UInt32(bitPattern: getRegister(src))
        let rotated = n == 0 ? value : (value << n) | (value >> (32 - n))
        setRegister(src, Int32(bitPattern: rotated))
        pc += 5
    }

    private func opRor() {
        let src = ram.memory[Int(ip) + 1]
        let n = UInt32(shiftAmount())
        let value = UInt32(bitPattern: getRegister(src))
        let rotated = n == 0 ? value : (value >> n) | (value << (32 - n))
        setRegister(src, Int32(bitPattern: rotated))
        pc += 5
    }

    private func opAnd(_ mode: AddressMode) {
        let dest = ram.memory[Int(ip) + 1]
        setRegister(dest, getRegister(dest) & binarySource(mode))
        pc += 5
    }

    private func opBor(_ mode: AddressMode) {
        let dest = ram.memory[Int(ip) + 1]
        setRegister(dest, getRegister(dest) | binarySource(mode))
        pc += 5
    }

    private func opXor(_ mode: AddressMode) {
        let dest = ram.memory[Int(ip) + 1]
        setRegister(dest, getRegister(dest) ^ binarySource(mode))
        pc += 5
    }

    private func opNot() {
        let src = ram.memory[Int(ip) + 1]
        setRegister(src, ~getRegister(src))
        pc += 5
    }

    // MARK: Flow control

    /// True when a single-operand instruction's operand is a register — the
    /// assembler always encodes that as RegVal for a single-operand
    /// mnemonic (param2 is never populated), never RegReg; both are
    /// accepted here purely because op.cs itself checks both, defensively.
    private func singleOperandIsRegister(_ mode: AddressMode) -> Bool {
        mode == .regReg || mode == .regVal
    }

    private func jumpTarget(_ mode: AddressMode) -> Int32 {
        singleOperandIsRegister(mode)
            ? getRegister(ram.memory[Int(ip) + 1])
            : get32BitParameter(at: Int(ip) + 2)
    }

    private func opJmp(_ mode: AddressMode) {
        pc = jumpTarget(mode)
    }

    private func opCll(_ mode: AddressMode) throws {
        try callStackCall(Int(pc) + 5)
        pc = jumpTarget(mode)
    }

    private func opRet() throws {
        pc = Int32(try callStackReturn())
    }

    private func opJmt(_ mode: AddressMode) {
        if lastLogic { pc = jumpTarget(mode) } else { pc += 5 }
    }

    private func opJmf(_ mode: AddressMode) {
        if !lastLogic { pc = jumpTarget(mode) } else { pc += 5 }
    }

    private func opClt(_ mode: AddressMode) throws {
        if lastLogic {
            try callStackCall(Int(pc) + 5)
            pc = jumpTarget(mode)
        } else {
            pc += 5
        }
    }

    private func opClf(_ mode: AddressMode) throws {
        if !lastLogic {
            try callStackCall(Int(pc) + 5)
            pc = jumpTarget(mode)
        } else {
            pc += 5
        }
    }

    // MARK: Tests — always register:register, regardless of encoded mode

    private func opTeq() {
        lastLogic = getRegister(ram.memory[Int(ip) + 1]) == getRegister(ram.memory[Int(ip) + 2])
        pc += 5
    }

    private func opTne() {
        lastLogic = getRegister(ram.memory[Int(ip) + 1]) != getRegister(ram.memory[Int(ip) + 2])
        pc += 5
    }

    private func opTlt() {
        lastLogic = getRegister(ram.memory[Int(ip) + 1]) < getRegister(ram.memory[Int(ip) + 2])
        pc += 5
    }

    private func opTmt() {
        lastLogic = getRegister(ram.memory[Int(ip) + 1]) > getRegister(ram.memory[Int(ip) + 2])
        pc += 5
    }

    // MARK: Stack — one byte at a time, in the shared address space

    private func opPsh(_ mode: AddressMode) throws {
        let value: UInt8 = mode == .regVal
            ? UInt8(truncatingIfNeeded: getRegister(ram.memory[Int(ip) + 1]))
            : ram.memory[Int(ip) + 1]
        sp -= 1
        try ram.setByte(Int(sp), value)
        pc += 5
    }

    private func opPop() throws {
        guard Int(sp) < ram.memory.count - 1 else { throw CacaVMError.stackUnderflow }
        let value = ram.getByte(Int(sp))
        sp += 1
        setRegister(ram.memory[Int(ip) + 1], Int32(value))
        pc += 5
    }

    // MARK: Interrupts

    private func opSwi() throws {
        try SoftwareInterrupts.handle(command: Int(get32BitParameter(at: Int(ip) + 2)), vm: self)
        pc += 5
    }

    private func opKei() throws {
        try KernelInterrupts.handle(command: Int(get32BitParameter(at: Int(ip) + 2)), vm: self)
        pc += 5
    }

    // MARK: Memory — direct (literal address) and register-indirect

    private func opMom(_ mode: AddressMode) throws {
        let destIsRegister = mode == .regReg || mode == .valReg
        let destAddr = destIsRegister
            ? Int(getRegister(ram.memory[Int(ip) + 2]))
            : Int(get32BitParameter(at: Int(ip) + 2))
        let srcVal: Int32 = (mode == .regVal || mode == .regReg)
            ? getRegister(ram.memory[Int(ip) + 1])
            : Int32(ram.memory[Int(ip) + 1])
        try ram.setByte(destAddr, UInt8(truncatingIfNeeded: srcVal))
        pc += 5
    }

    private func opMoe(_ mode: AddressMode) throws {
        let srcIsRegister = mode == .regReg || mode == .valReg
        let srcAddr = srcIsRegister
            ? Int(getRegister(ram.memory[Int(ip) + 2]))
            : Int(get32BitParameter(at: Int(ip) + 2))
        let destReg = ram.memory[Int(ip) + 1]
        setRegister(destReg, Int32(ram.getByte(srcAddr)))
        pc += 5
    }

    // MARK: CallStack bridge (VM owns the instance; these just forward to it)

    private func callStackCall(_ location: Int) throws {
        try callStack.call(location)
    }

    private func callStackReturn() throws -> Int {
        try callStack.returnAddress()
    }
}
