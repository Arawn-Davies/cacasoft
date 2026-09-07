import XCTest
@testable import CacaVMKit

/// Mirrors Caca.VM.Tests/RoundTripTests.cs's compile → decompile → recompile
/// checks — this Swift port has never had its own decompiler test coverage
/// before, unlike the VM opcodes themselves (see VMTests.swift).
final class RoundTripTests: XCTestCase {
    private func execute(_ code: [UInt8], ramSize: Int = 65536) throws -> String {
        let console = BufferConsole()
        let vm = VM(program: code, ramSize: ramSize, console: console)
        try vm.run()
        return console.output
    }

    /// Regression for the JMP/CLL/JMT/JMF/CLT/CLF decompiler truncation bug: a
    /// jump target's real byte offset (780, 0x30C) is chosen so its low byte
    /// (0x0C) collides with the offset of a "wrong" label 12 bytes into the
    /// program. Before the fix, a literal jump target was rendered through the
    /// same byte-truncating format as PSH's own one-byte literal, so
    /// decompiling this program and recompiling the result would silently
    /// retarget the jump at "wrong" instead of "target" — the VM's execution
    /// of the ORIGINAL bytecode was never affected (it reads the target
    /// untruncated); only what the decompiler produced, and hence what
    /// recompiling that text produces, was wrong.
    ///
    /// The filler between "wrong" and "target" is 125 real (never-executed —
    /// the unconditional JMP skips clean over all of it) MOV instructions,
    /// not raw zero bytes: a run of zero bytes decodes as opcode 0x00, which
    /// the decompiler treats as "end of program" and stops at, silently
    /// dropping "target"'s own instructions from the decompiled text — a
    /// genuine but separate decompiler limitation, unrelated to the bug this
    /// test is meant to catch.
    func testJumpTargetPast255BytesFunctionalRoundTripOutputMatches() throws {
        let filler = String(repeating: "MOV AL, 0x01\n", count: 125)
        let source = """
        MOV AL, 0x01
        JMP target
        wrong:
        MOV AH, 'N'
        KEI 0x01
        KEI 0x02
        \(filler)target:
        MOV AH, 'Y'
        KEI 0x01
        KEI 0x02
        """

        let original = try Assembler.assemble(source)
        let decompiled = try Decompiler.decompile(original)
        let roundTrip = try Assembler.assemble(decompiled)

        let output1 = try execute(original)
        let output2 = try execute(roundTrip)

        XCTAssertEqual(output1, "YHalting!\n")
        XCTAssertEqual(output1, output2)
    }
}
