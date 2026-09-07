import Foundation

/// SWI 0x01 — a from-scratch port of SoftwareInterrupts.cs, AL=0x03/0x04
/// included (this session's own additions: strcmp and atoi).
enum SoftwareInterrupts {
    static func handle(command: Int, vm: VM) throws {
        guard command == 0x01 else {
            throw CacaVMError.unknownSoftwareInterrupt(command: command)
        }

        switch vm.al {
        case 0x01:
            try strlen(vm: vm)
        case 0x02:
            try strcpy(vm: vm)
        case 0x03:
            try strcmp(vm: vm)
        case 0x04:
            try atoi(vm: vm)
        default:
            throw CacaVMError.unknownSoftwareInterrupt(command: Int(vm.al))
        }
    }

    /// X = address of a null-terminated string; B = length written.
    private static func strlen(vm: VM) throws {
        let addr = Int(vm.x)
        let limit = vm.ram.memory.count
        var len = 0
        while addr + len < limit, vm.ram.getByte(addr + len) != 0x00 {
            len += 1
        }
        vm.setRegister(UInt8(VM.Register.b.rawValue), Int32(len))
    }

    /// X = source address, Y = destination address, B = byte count.
    private static func strcpy(vm: VM) throws {
        let src = Int(vm.x)
        let dst = Int(vm.y)
        let count = Int(vm.getRegister(UInt8(VM.Register.b.rawValue)))
        let limit = vm.ram.memory.count
        guard src >= 0, src + count <= limit, dst >= 0, dst + count <= limit else {
            throw CacaVMError.protectedMemory(location: max(src, dst))
        }
        vm.ram.setSection(dst, vm.ram.getSection(src, count))
    }

    /// X, Y = addresses of two null-terminated strings; B = 1 if equal, 0 if
    /// not. Equality only, not lexicographic ordering — B is an unsigned
    /// composite (getSplit never carries a sign), so there is no way to
    /// return "less than zero" through it correctly.
    private static func strcmp(vm: VM) throws {
        let limit = vm.ram.memory.count
        var equal = true
        var i = 0
        while true {
            let xAddr = Int(vm.x) + i
            let yAddr = Int(vm.y) + i
            guard xAddr >= 0, xAddr < limit, yAddr >= 0, yAddr < limit else {
                throw CacaVMError.protectedMemory(location: max(xAddr, yAddr))
            }
            let xByte = vm.ram.getByte(xAddr)
            let yByte = vm.ram.getByte(yAddr)
            if xByte != yByte { equal = false; break }
            if xByte == 0x00 { break }
            i += 1
        }
        vm.setRegister(UInt8(VM.Register.b.rawValue), equal ? 1 : 0)
    }

    /// X = address of a null-terminated decimal string; Y = the parsed
    /// signed 32-bit value. An optional leading +/-, then digits, stopping
    /// at the first non-digit or the terminator. No digits at all is
    /// defined as Y = 0, matching C's own atoi convention.
    private static func atoi(vm: VM) throws {
        let limit = vm.ram.memory.count
        var addr = Int(vm.x)
        guard addr >= 0, addr < limit else {
            throw CacaVMError.protectedMemory(location: addr)
        }

        var negative = false
        let first = vm.ram.getByte(addr)
        if first == UInt8(ascii: "-") || first == UInt8(ascii: "+") {
            negative = first == UInt8(ascii: "-")
            addr += 1
        }

        var result: Int32 = 0
        var sawDigit = false
        while addr < limit {
            let b = vm.ram.getByte(addr)
            guard b >= UInt8(ascii: "0"), b <= UInt8(ascii: "9") else { break }
            result = (result &* 10) &+ Int32(b - UInt8(ascii: "0"))
            sawDigit = true
            addr += 1
        }

        vm.y = sawDigit ? (negative ? 0 &- result : result) : 0
    }
}
