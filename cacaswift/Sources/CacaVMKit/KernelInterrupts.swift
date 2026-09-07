import Foundation

/// KEI (opcode 0x2B) — a from-scratch port of KernelInterrupts.cs, AL=0x06
/// included (this session's own addition to the standard library: signed
/// 32-bit print from X, via a real ToString-equivalent rather than the
/// unsigned 16-bit AL=0x05).
enum KernelInterrupts {
    static func handle(command: Int, vm: VM) throws {
        switch command {
        case 0x01:
            try handleStdio(vm: vm)
        case 0x02:
            vm.console.writeLine("Halting!")
            vm.running = false
        default:
            throw CacaVMError.unknownInterrupt(command: command)
        }
    }

    private static func handleStdio(vm: VM) throws {
        switch vm.al {
        case 0x01:
            // Write-character: the byte in AH.
            vm.console.write(String(UnicodeScalar(vm.ah)))
        case 0x02:
            // Write-string: X = base address, B = byte count.
            let length = Int(vm.getRegister(UInt8(VM.Register.b.rawValue)))
            let bytes = vm.ram.getSection(Int(vm.x), length)
            vm.console.write(String(bytes.map { Character(UnicodeScalar($0)) }))
        case 0x03:
            // Read-character into AH.
            vm.ah = try vm.console.read()
        case 0x04:
            // Read-line: raw bytes into RAM at X; B = length written.
            let line = try vm.console.readLine()
            let bytes = Array(line.utf8)
            vm.setRegister(UInt8(VM.Register.b.rawValue), Int32(bytes.count))
            vm.ram.setSection(Int(vm.x), bytes)
        case 0x05:
            // Write-integer: B (unsigned 16-bit composite) as decimal.
            vm.console.write(String(vm.getRegister(UInt8(VM.Register.b.rawValue))))
        case 0x06:
            // Write-signed-integer: the full signed 32-bit value of X.
            vm.console.write(String(vm.x))
        default:
            break
        }
    }
}
