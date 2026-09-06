import XCTest
@testable import CacaVMKit

/// Mirrors the key cases from Caca.VM.Tests/VmExecutionTests.cs — the same
/// programs, run through this Swift port instead, to prove it agrees with
/// the C# original rather than just compiling.
final class VMTests: XCTestCase {
    private func runCIL(_ source: String, ramSize: Int = 65536, input: [String] = []) throws -> (VM, BufferConsole) {
        let code = try Assembler.assemble(source)
        let console = BufferConsole(input: input)
        let vm = VM(program: code, ramSize: ramSize, console: console)
        try vm.run()
        return (vm, console)
    }

    func testHelloWorldDb() throws {
        let source = """
        JMP main
        hello:
        DB "Hello, World", 0x0A, 0x00
        main:
        MOV AL, 0x02
        MOV X, hello
        MOV BL, 13
        KEI 0x01
        KEI 0x02
        """
        let (_, console) = try runCIL(source)
        XCTAssertEqual(console.output, "Hello, World\nHalting!\n")
    }

    func testArithmeticWraparound() throws {
        let source = """
        MOV X, 2147483647
        ADD X, 1
        MOV AL, 0x06
        KEI 0x01
        KEI 0x02
        """
        let (_, console) = try runCIL(source)
        XCTAssertEqual(console.output, "-2147483648Halting!\n")
    }

    func testBitwiseOps() throws {
        let source = """
        MOV X, 0x0F
        ROL X, 4
        MOV AL, 0x06
        KEI 0x01
        ROR X, 4
        KEI 0x01
        MOV X, 0
        NOT X
        KEI 0x01
        KEI 0x02
        """
        let (_, console) = try runCIL(source)
        XCTAssertEqual(console.output, "240" + "15" + "-1" + "Halting!\n")
    }

    /// Regression for the CallStack off-by-one: calls the same subroutine
    /// TWICE. A fix that only handles a single CLL/RET pair would still
    /// pass a single-call test but fail this one.
    func testCallReturnSecondCallAfterReturn() throws {
        let source = """
        JMP main
        sub:
        MOV AL, 0x01
        MOV AH, 'A'
        KEI 0x01
        RET
        main:
        CLL sub
        CLL sub
        KEI 0x02
        """
        let (_, console) = try runCIL(source)
        XCTAssertEqual(console.output, "AAHalting!\n")
    }

    /// Genuine recursion: factorial(6) via CLL/RET, argument on the stack,
    /// result in X, with the save/restore-around-the-recursive-call
    /// discipline factorial_recursive.cil documents.
    func testFactorialRecursion() throws {
        let source = """
        JMP main
        main:
        MOV X, 6
        PSH X
        CLL factorial
        MOV AL, 0x06
        KEI 0x01
        KEI 0x02
        factorial:
        POP Y
        MOV X, 2
        TLT Y, X
        JMF recurse
        MOV X, 1
        RET
        recurse:
        PSH Y
        MOV X, Y
        DEC X
        PSH X
        CLL factorial
        POP Y
        MUL X, Y
        RET
        """
        let (_, console) = try runCIL(source)
        XCTAssertEqual(console.output, "720Halting!\n")
    }

    /// Register-indirect MOM/MOE, proven genuinely indirect (not just a
    /// second flavor of immediate) by addressing two different locations
    /// from two different runtime values of the same register.
    func testIndirectMemoryTwoDifferentAddresses() throws {
        let source = """
        MOV X, 0x60
        MOV AL, 'A'
        MOM AL, X
        MOV X, 0x70
        MOV AL, 'B'
        MOM AL, X
        MOV X, 0x60
        MOE AH, X
        MOV X, 0x70
        MOE BH, X
        KEI 0x02
        """
        let (vm, _) = try runCIL(source)
        XCTAssertEqual(vm.ah, UInt8(ascii: "A"))
        XCTAssertEqual(vm.bh, UInt8(ascii: "B"))
    }

    func testStackPushPop() throws {
        let source = """
        MOV X, 10
        PSH X
        MOV X, 20
        PSH X
        MOV X, 30
        PSH X
        POP Y
        MOV AL, 0x06
        MOV X, Y
        KEI 0x01
        POP Y
        MOV X, Y
        KEI 0x01
        POP Y
        MOV X, Y
        KEI 0x01
        KEI 0x02
        """
        let (_, console) = try runCIL(source)
        XCTAssertEqual(console.output, "302010Halting!\n")
    }

    func testStackOverflowIntoCodeThrows() throws {
        var source = "MOV AL, 0x42\n"
        for _ in 0..<100 { source += "PSH AL\n" }
        XCTAssertThrowsError(try runCIL(source, ramSize: 626)) { error in
            XCTAssertTrue("\(error)".contains("overwrite its own code"))
        }
    }

    func testStackUnderflowThrows() throws {
        XCTAssertThrowsError(try runCIL("POP AL\nKEI 0x02\n")) { error in
            XCTAssertTrue("\(error)".contains("empty stack"))
        }
    }

    func testDivisionByZeroThrows() throws {
        XCTAssertThrowsError(try runCIL("MOV X, 1\nMOV Y, 0\nDIV X, Y\nKEI 0x02\n")) { error in
            XCTAssertTrue("\(error)".contains("division by zero"))
        }
    }

    // MARK: Standard library

    func testKeiWriteSignedInt() throws {
        for (value, expected) in [(42, "42"), (-17, "-17"), (0, "0"),
                                   (Int(Int32.max), "2147483647"), (Int(Int32.min), "-2147483648")] {
            let source = "MOV X, \(value)\nMOV AL, 0x06\nKEI 0x01\nKEI 0x02\n"
            let (_, console) = try runCIL(source)
            XCTAssertEqual(console.output, expected + "Halting!\n")
        }
    }

    /// Label names deliberately avoid A/B/C/X/Y/SP/SS/PC/IP: the assembler
    /// checks register names before labels (matching Compiler.cs exactly),
    /// so a label named "a" would be shadowed by the register A — caught
    /// only by actually running this, not by reading the port's source.
    func testSwiStrcmpEqualAndDifferent() throws {
        let source = """
        JMP main
        strMatch1:
        DB "match", 0x00
        strMatch2:
        DB "match", 0x00
        strDiff:
        DB "nomatch", 0x00
        main:
        MOV AL, 0x03
        MOV X, strMatch1
        MOV Y, strMatch2
        SWI 0x01
        MOV X, B
        MOV AL, 0x06
        KEI 0x01
        MOV AL, 0x03
        MOV X, strMatch1
        MOV Y, strDiff
        SWI 0x01
        MOV X, B
        MOV AL, 0x06
        KEI 0x01
        KEI 0x02
        """
        let (_, console) = try runCIL(source)
        XCTAssertEqual(console.output, "10Halting!\n")
    }

    func testSwiAtoiPositiveAndNegative() throws {
        let source = """
        JMP main
        pos:
        DB "42", 0x00
        neg:
        DB "-17", 0x00
        main:
        MOV AL, 0x04
        MOV X, pos
        SWI 0x01
        MOV X, Y
        MOV AL, 0x06
        KEI 0x01
        MOV AL, 0x04
        MOV X, neg
        SWI 0x01
        MOV X, Y
        MOV AL, 0x06
        KEI 0x01
        KEI 0x02
        """
        let (_, console) = try runCIL(source)
        XCTAssertEqual(console.output, "42-17Halting!\n")
    }

    func testInteractiveReadLineAndAtoi() throws {
        let source = """
        JMP main
        main:
        MOV AL, 0x04
        MOV X, 0x2000
        KEI 0x01
        MOV Y, B
        MOV X, 0x2000
        ADD X, Y
        MOM 0x00, X
        MOV AL, 0x04
        MOV X, 0x2000
        SWI 0x01
        ADD Y, Y
        MOV X, Y
        MOV AL, 0x06
        KEI 0x01
        KEI 0x02
        """
        let (_, console) = try runCIL(source, input: ["5"])
        XCTAssertEqual(console.output, "10Halting!\n")
    }
}
