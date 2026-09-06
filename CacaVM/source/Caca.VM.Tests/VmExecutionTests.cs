using System;
using System.Threading.Tasks;
using Xunit;
using Caca.VM;
using Caca.VM.Compiler;

namespace Caca.VM.Tests
{
    /// <summary>
    /// Tests that compile CIL assembly programs, execute them in the VM, and assert on
    /// console output or register state. All tests are run sequentially (via
    /// <see cref="VmCollection"/>) because they share the process-wide
    /// <see cref="Caca.VM.Globals.console"/> singleton.
    /// </summary>
    [Collection("VM")]
    public sealed class VmExecutionTests
    {
        private readonly TestConsole _console = new TestConsole();

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Compiles <paramref name="source"/>, loads it into a fresh VM and executes it.
        /// Returns the VM so callers can inspect registers.
        /// </summary>
        private VM CompileAndRun(string source, int ramSize = Globals.DefaultRamSize)
        {
            byte[] code = new global::Caca.VM.Compiler.Compiler(source).Compile();

            Caca.VM.Globals.console = _console;
            Caca.VM.Globals.DebugMode = false;
            _console.Reset();

            var vm = new VM(code, ramSize);
            vm.Execute();
            return vm;
        }

        // ── Hello, World! ────────────────────────────────────────────────────────

        /// <summary>
        /// Classic "Hello, World!" program — prints each character individually via
        /// KEI 0x01 in write-char mode (AL = 0x01, character in AH).
        /// </summary>
        [Fact]
        public void HelloWorld_OutputsHelloWorldNewline()
        {
            const string source = @"
MOV AL, 0x01
MOV AH, 'H'
KEI 0x01
MOV AH, 'e'
KEI 0x01
MOV AH, 'l'
KEI 0x01
MOV AH, 'l'
KEI 0x01
MOV AH, 'o'
KEI 0x01
MOV AH, ','
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, 'W'
KEI 0x01
MOV AH, 'o'
KEI 0x01
MOV AH, 'r'
KEI 0x01
MOV AH, 'l'
KEI 0x01
MOV AH, 'd'
KEI 0x01
MOV AH, '!'
KEI 0x01
MOV AH, '\n'
KEI 0x01
KEI 0x02
";
            CompileAndRun(source);

            // KEI 0x02 always writes "Halting!\n" before stopping.
            Assert.Equal("Hello, World!\nHalting!\n", _console.Output);
        }

        // ── Arithmetic ───────────────────────────────────────────────────────────

        /// <summary>ADD: AL = 10 + 5 → register should contain 15 after execution.</summary>
        [Fact]
        public void Add_ProducesCorrectResult()
        {
            const string source = @"
MOV AL, 10
ADD AL, 5
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(15, vm.AL);
        }

        /// <summary>SUB: AL = 20 − 7 → register should contain 13.</summary>
        [Fact]
        public void Subtract_ProducesCorrectResult()
        {
            const string source = @"
MOV AL, 20
SUB AL, 7
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(13, vm.AL);
        }

        /// <summary>MUL: AL = 6 × 7 → register should contain 42.</summary>
        [Fact]
        public void Multiply_ProducesCorrectResult()
        {
            const string source = @"
MOV AL, 6
MUL AL, 7
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(42, vm.AL);
        }

        /// <summary>INC / DEC: AL starts at 5, incremented to 6 then decremented to 5.</summary>
        [Fact]
        public void IncDec_RoundTripsValue()
        {
            const string source = @"
MOV AL, 5
INC AL
DEC AL
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(5, vm.AL);
        }

        // ── Register copy ────────────────────────────────────────────────────────

        /// <summary>MOV reg, reg: copy AL into AH and verify both hold the same value.</summary>
        [Fact]
        public void MovRegReg_CopiesValue()
        {
            const string source = @"
MOV AL, 0x42
MOV AH, AL
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(0x42, vm.AL);
            Assert.Equal(0x42, vm.AH);
        }

        // ── Conditional jump ─────────────────────────────────────────────────────

        /// <summary>
        /// TEQ + JMT: when AL == AH the jump fires, skipping the MOV that would
        /// change AL to 0xFF.  After execution AL must still equal 3.
        /// </summary>
        [Fact]
        public void ConditionalJump_SkipsInstructionWhenEqual()
        {
            const string source = @"
MOV AL, 3
MOV AH, 3
TEQ AL, AH
JMT skip
MOV AL, 0xFF
skip:
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(3, vm.AL);
        }

        /// <summary>
        /// TEQ + JMT: when AL != AH (values differ) TEQ sets the condition flag to false,
        /// so JMT does NOT fire (JMT fires only when the flag is true).  Execution falls
        /// through to the MOV that sets AL to 0xFF.
        /// </summary>
        [Fact]
        public void ConditionalJump_FallsThroughWhenNotEqual()
        {
            const string source = @"
MOV AL, 1
MOV AH, 2
TEQ AL, AH
JMT skip
MOV AL, 0xFF
skip:
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(0xFF, vm.AL);
        }

        // ── Stack ────────────────────────────────────────────────────────────────

        /// <summary>
        /// PSH / POP: push 0x42 onto the stack, zero AL, then pop back.
        /// AL must equal 0x42 after the pop.
        /// </summary>
        [Fact]
        public void StackPushPop_RestoresValue()
        {
            const string source = @"
MOV AL, 0x42
PSH AL
MOV AL, 0x00
POP AL
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(0x42, vm.AL);
        }

        /// <summary>
        /// PSHN <c>0x10</c> must move SP by exactly 16 (matching 16 individual
        /// PSH 0s) and zero every one of those bytes.
        /// </summary>
        [Fact]
        public void Pshn_ReservesAndZeroesExactlyNBytes()
        {
            const string source = @"
MOV Y, SP
PSHN 0x10
MOV X, SP
MOE AH, X
SUB Y, X
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(16, vm.Y); // SP moved by exactly 16
            Assert.Equal(0, vm.AH); // the reserved byte at the new SP is zero
        }

        /// <summary>
        /// PSHN then POPN of the same count must restore SP exactly, and a
        /// value pushed before PSHN must survive the round trip unchanged —
        /// proving POPN discards precisely what PSHN reserved, no more, no
        /// less.
        /// </summary>
        [Fact]
        public void Pshn_ThenPopn_RestoresStackPointerAndSurvivingValue()
        {
            const string source = @"
MOV AL, 0x42
PSH AL
MOV Y, SP
PSHN 0x10
POPN 0x10
MOV X, SP
SUB X, Y
MOV AL, 0x00
POP AL
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(0, vm.X);    // SP back to exactly where it was before PSHN
            Assert.Equal(0x42, vm.AL); // the value pushed before PSHN survived intact
        }

        /// <summary>PSHN into protected (own-code) memory throws, exactly like a run of individual PSHes would.</summary>
        [Fact]
        public void Pshn_IntoOwnCode_Throws()
        {
            const string source = @"
MOV AL, 0x42
PSHN 0x32
KEI 0x02
";
            var c = new Compiler.Compiler(source);
            byte[] code = c.Compile();
            var vm = new VM(code, 20);
            Globals.console = _console;
            Globals.DebugMode = false;
            Assert.Throws<Exception>(() => vm.Execute());
        }

        /// <summary>POPN on an empty stack throws, exactly like a bare POP would.</summary>
        [Fact]
        public void Popn_OnEmptyStack_Throws()
        {
            const string source = @"
POPN 0x05
KEI 0x02
";
            Assert.Throws<Exception>(() => CompileAndRun(source));
        }

        // ── Bitwise ──────────────────────────────────────────────────────────────

        /// <summary>AND: 0xFF &amp; 0x0F = 0x0F.</summary>
        [Fact]
        public void BitwiseAnd_ProducesCorrectResult()
        {
            const string source = @"
MOV AL, 0xFF
AND AL, 0x0F
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(0x0F, vm.AL);
        }

        /// <summary>BOR: 0xF0 | 0x0F = 0xFF.</summary>
        [Fact]
        public void BitwiseOr_ProducesCorrectResult()
        {
            const string source = @"
MOV AL, 0xF0
BOR AL, 0x0F
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(0xFF, vm.AL);
        }

        /// <summary>XOR: 0xFF ^ 0xFF = 0x00.</summary>
        [Fact]
        public void BitwiseXor_ProducesCorrectResult()
        {
            const string source = @"
MOV AL, 0xFF
XOR AL, 0xFF
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(0x00, vm.AL);
        }

        // ── Write-string mode ────────────────────────────────────────────────────

        /// <summary>
        /// KEI 0x01 in write-string mode (AL = 0x02): stores "Hi" in RAM via MOM,
        /// points X at the data address, sets B = 2, then issues KEI 0x01.
        /// Output (excluding halt) must equal "Hi".
        /// </summary>
        [Fact]
        public void WriteString_OutputsCorrectText()
        {
            // We'll store "Hi" at address 0x60 (96) in RAM using MOM (move-to-memory).
            // MOM src, addr  – writes the byte value of src to the given memory address.
            const string source = @"
; Store 'H' at 0x60, 'i' at 0x61
MOM 0x48, 0x60
MOM 0x69, 0x61
; Set up registers: AL=0x02 (write-string mode), X=base address, B=length
MOV AL, 0x02
MOV X, 0x60
MOV BL, 2
KEI 0x01
KEI 0x02
";
            CompileAndRun(source);
            Assert.Equal("HiHalting!\n", _console.Output);
        }

        // ── SWI 0x01 — String utilities ─────────────────────────────────────────

        /// <summary>
        /// SWI 0x01 AL=0x01 (strlen): stores a null-terminated string "AIL\0" in RAM
        /// (an arbitrary 3-letter sample, unrelated to product naming), points X at
        /// it, calls SWI, and asserts B == 3.
        /// </summary>
        [Fact]
        public void SwiStrlen_ReturnsCorrectLength()
        {
            // Store "AIL\0" at 0x80: 0x41='A', 0x49='I', 0x4C='L', 0x00=null terminator
            const string source = @"
MOM 0x41, 0x80
MOM 0x49, 0x81
MOM 0x4C, 0x82
MOM 0x00, 0x83
MOV AL, 0x01
MOV X, 0x80
SWI 0x01
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(3, vm.GetSplit('B'));
        }

        /// <summary>
        /// SWI 0x01 AL=0x02 (strcpy): copies 3 bytes from address 0x80 to 0x90,
        /// then reads them back via KEI write-string and asserts the output.
        /// </summary>
        [Fact]
        public void SwiStrcpy_CopiesBytes()
        {
            // Source "Hi!" at 0x80; copy 3 bytes to 0x90; print from 0x90.
            const string source = @"
MOM 0x48, 0x80
MOM 0x69, 0x81
MOM 0x21, 0x82
MOV AL, 0x02
MOV X, 0x80
MOV Y, 0x90
MOV BL, 3
SWI 0x01
MOV AL, 0x02
MOV X, 0x90
MOV BL, 3
KEI 0x01
KEI 0x02
";
            CompileAndRun(source);
            Assert.Equal("Hi!Halting!\n", _console.Output);
        }

        /// <summary>SWI 0x01 AL=0x03 (strcmp): two identical null-terminated strings compare equal (B=1).</summary>
        [Fact]
        public void SwiStrcmp_EqualStrings_SetsBToOne()
        {
            const string source = @"
MOM 0x48, 0x80
MOM 0x69, 0x81
MOM 0x00, 0x82
MOM 0x48, 0x90
MOM 0x69, 0x91
MOM 0x00, 0x92
MOV AL, 0x03
MOV X, 0x80
MOV Y, 0x90
SWI 0x01
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(1, vm.GetSplit('B'));
        }

        /// <summary>
        /// SWI 0x01 AL=0x03 (strcmp): a difference partway through the strings, and a
        /// difference in length alone (one string a strict prefix of the other, so the
        /// shorter one hits its own terminator while the longer one still has a byte),
        /// both compare unequal (B=0).
        /// </summary>
        [Fact]
        public void SwiStrcmp_DifferentStrings_SetsBToZero()
        {
            const string source = @"
MOM 0x48, 0x80
MOM 0x69, 0x81
MOM 0x00, 0x82
MOM 0x48, 0x90
MOM 0x6F, 0x91
MOM 0x00, 0x92
MOV AL, 0x03
MOV X, 0x80
MOV Y, 0x90
SWI 0x01
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(0, vm.GetSplit('B'));
        }

        [Fact]
        public void SwiStrcmp_PrefixOfLongerString_SetsBToZero()
        {
            // "Hi\0" at 0x80 vs "Hi!\0" at 0x90 — same first two bytes, but the first
            // string terminates where the second still has a byte.
            const string source = @"
MOM 0x48, 0x80
MOM 0x69, 0x81
MOM 0x00, 0x82
MOM 0x48, 0x90
MOM 0x69, 0x91
MOM 0x21, 0x92
MOM 0x00, 0x93
MOV AL, 0x03
MOV X, 0x80
MOV Y, 0x90
SWI 0x01
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(0, vm.GetSplit('B'));
        }

        /// <summary>SWI 0x01 AL=0x04 (atoi): a plain positive decimal string parses into Y.</summary>
        [Fact]
        public void SwiAtoi_PositiveNumber_ParsesIntoY()
        {
            // "42\0" at 0x80.
            const string source = @"
MOM 0x34, 0x80
MOM 0x32, 0x81
MOM 0x00, 0x82
MOV AL, 0x04
MOV X, 0x80
SWI 0x01
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(42, vm.Y);
        }

        /// <summary>SWI 0x01 AL=0x04 (atoi): a leading '-' negates the parsed value.</summary>
        [Fact]
        public void SwiAtoi_NegativeNumber_ParsesIntoY()
        {
            // "-17\0" at 0x80.
            const string source = @"
MOM 0x2D, 0x80
MOM 0x31, 0x81
MOM 0x37, 0x82
MOM 0x00, 0x83
MOV AL, 0x04
MOV X, 0x80
SWI 0x01
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(-17, vm.Y);
        }

        /// <summary>
        /// SWI 0x01 AL=0x04 (atoi): parsing stops at the first non-digit rather than
        /// failing, matching how C's own atoi treats trailing garbage.
        /// </summary>
        [Fact]
        public void SwiAtoi_StopsAtFirstNonDigit()
        {
            // "12x\0" at 0x80.
            const string source = @"
MOM 0x31, 0x80
MOM 0x32, 0x81
MOM 0x78, 0x82
MOM 0x00, 0x83
MOV AL, 0x04
MOV X, 0x80
SWI 0x01
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(12, vm.Y);
        }

        /// <summary>
        /// SWI 0x01 AL=0x04 (atoi): no digits at all — an empty string, or one that
        /// starts with neither a sign nor a digit — is defined as Y=0, not an error.
        /// </summary>
        [Fact]
        public void SwiAtoi_NoDigits_ResultIsZero()
        {
            // "x\0" at 0x80.
            const string source = @"
MOM 0x78, 0x80
MOM 0x00, 0x81
MOV AL, 0x04
MOV X, 0x80
SWI 0x01
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal(0, vm.Y);
        }

        // ── KEI 0x01 AL=0x06 — write signed integer ──────────────────────────────

        /// <summary>KEI 0x01 AL=0x06: prints the signed 32-bit value of X, negative included.</summary>
        [Theory]
        [InlineData(42, "42")]
        [InlineData(-17, "-17")]
        [InlineData(0, "0")]
        [InlineData(int.MaxValue, "2147483647")]
        [InlineData(int.MinValue, "-2147483648")]
        public void KeiWriteSignedInt_PrintsSignedDecimal(int value, string expected)
        {
            string source = $@"
MOV X, {value}
MOV AL, 0x06
KEI 0x01
KEI 0x02
";
            CompileAndRun(source);
            Assert.Equal(expected + "Halting!\n", _console.Output);
        }

        // ── Compiler error handling ──────────────────────────────────────────────

        /// <summary>The compiler must reject an unknown mnemonic with a <see cref="BuildException"/>.</summary>
        [Fact]
        public void Compiler_ThrowsOnUnknownMnemonic()
        {
            Assert.Throws<BuildException>(() => new global::Caca.VM.Compiler.Compiler("FOOBAR AL, 1").Compile());
        }

        /// <summary>An empty source string must throw a <see cref="BuildException"/>.</summary>
        [Fact]
        public void Compiler_ThrowsOnEmptySource()
        {
            Assert.Throws<BuildException>(() => new global::Caca.VM.Compiler.Compiler("").Compile());
        }

        // ── Hello, World! via DB directive ───────────────────────────────────────

        /// <summary>
        /// Hello World using a DB string literal: the string data is defined inline
        /// in the source (like the x86 <c>db</c> directive), then printed via
        /// KEI 0x01 in write-string mode (AL = 0x02, X = address, BL = length).
        ///
        /// Program layout:
        ///   JMP main            ; skip over data
        ///   hello:
        ///   DB "Hello, World", 0x0A, 0x00   ; 14 bytes of data
        ///   main:
        ///   MOV AL, 0x02 / MOV X, hello / MOV BL, 13 / KEI 0x01 / KEI 0x02
        /// </summary>
        [Fact]
        public void HelloWorldDb_OutputsHelloWorldNewline()
        {
            const string source = @"
JMP main

hello:
DB ""Hello, World"", 0x0A, 0x00

main:
MOV AL, 0x02
MOV X, hello
MOV BL, 13
KEI 0x01
KEI 0x02
";
            CompileAndRun(source);
            Assert.Equal("Hello, World\nHalting!\n", _console.Output);
        }

        // ── Simple calculator ────────────────────────────────────────────────────

        /// <summary>
        /// Simple calculator: computes 3 + 4, prints "3 + 4 = 7" followed by a
        /// newline, then halts.  The result (7) is printed via KEI 0x01 AL=0x05
        /// which writes register B as a decimal integer.
        /// </summary>
        [Fact]
        public void Calculator_Addition_PrintsExpression()
        {
            const string source = @"
; Print ""3 + 4 = ""
MOV AL, 0x01
MOV AH, '3'
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, '+'
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, '4'
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, '='
KEI 0x01
MOV AH, ' '
KEI 0x01
; Compute 3 + 4 into BL
MOV BL, 3
ADD BL, 4
; Print result (register B) as a decimal integer
MOV AL, 0x05
KEI 0x01
; Print newline
MOV AL, 0x01
MOV AH, 0x0A
KEI 0x01
KEI 0x02
";
            CompileAndRun(source);
            Assert.Equal("3 + 4 = 7\nHalting!\n", _console.Output);
        }

        /// <summary>
        /// Calculator subtraction: 10 - 3 = 7.
        /// </summary>
        [Fact]
        public void Calculator_Subtraction_PrintsExpression()
        {
            const string source = @"
MOV AL, 0x01
MOV AH, '1'
KEI 0x01
MOV AH, '0'
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, '-'
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, '3'
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, '='
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV BL, 10
SUB BL, 3
MOV AL, 0x05
KEI 0x01
MOV AL, 0x01
MOV AH, 0x0A
KEI 0x01
KEI 0x02
";
            CompileAndRun(source);
            Assert.Equal("10 - 3 = 7\nHalting!\n", _console.Output);
        }

        /// <summary>
        /// Calculator multiplication: 6 * 7 = 42.
        /// </summary>
        [Fact]
        public void Calculator_Multiplication_PrintsExpression()
        {
            const string source = @"
MOV AL, 0x01
MOV AH, '6'
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, '*'
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, '7'
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, '='
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV BL, 6
MUL BL, 7
MOV AL, 0x05
KEI 0x01
MOV AL, 0x01
MOV AH, 0x0A
KEI 0x01
KEI 0x02
";
            CompileAndRun(source);
            Assert.Equal("6 * 7 = 42\nHalting!\n", _console.Output);
        }

        /// <summary>
        /// Calculator division: 20 / 4 = 5.
        /// </summary>
        [Fact]
        public void Calculator_Division_PrintsExpression()
        {
            const string source = @"
MOV AL, 0x01
MOV AH, '2'
KEI 0x01
MOV AH, '0'
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, '/'
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, '4'
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV AH, '='
KEI 0x01
MOV AH, ' '
KEI 0x01
MOV BL, 20
DIV BL, 4
MOV AL, 0x05
KEI 0x01
MOV AL, 0x01
MOV AH, 0x0A
KEI 0x01
KEI 0x02
";
            CompileAndRun(source);
            Assert.Equal("20 / 4 = 5\nHalting!\n", _console.Output);
        }

        /// <summary>
        /// Full calculator: runs ADD, SUB, MUL, and DIV in a single program to verify
        /// that all four arithmetic operations execute correctly in the same executable.
        /// Each result is printed on its own line via KEI 0x01 AL=0x05 (write-integer mode).
        /// Expected output: "7\n7\n42\n5\nHalting!\n"
        /// </summary>
        [Fact]
        public void Calculator_AllOperations_PrintsAllResults()
        {
            const string source = @"
; ADD: 3 + 4 = 7
MOV BL, 3
ADD BL, 4
MOV AL, 0x05
KEI 0x01
MOV AL, 0x01
MOV AH, 0x0A
KEI 0x01
; SUB: 10 - 3 = 7
MOV BL, 10
SUB BL, 3
MOV AL, 0x05
KEI 0x01
MOV AL, 0x01
MOV AH, 0x0A
KEI 0x01
; MUL: 6 * 7 = 42
MOV BL, 6
MUL BL, 7
MOV AL, 0x05
KEI 0x01
MOV AL, 0x01
MOV AH, 0x0A
KEI 0x01
; DIV: 20 / 4 = 5
MOV BL, 20
DIV BL, 4
MOV AL, 0x05
KEI 0x01
MOV AL, 0x01
MOV AH, 0x0A
KEI 0x01
KEI 0x02
";
            CompileAndRun(source);
            Assert.Equal("7\n7\n42\n5\nHalting!\n", _console.Output);
        }

        /// <summary>
        /// Regression test for the PC/IP byte-width bug: PC and IP were declared as
        /// <c>byte</c> (max 255), so any program whose code exceeded ~256 bytes wrapped
        /// the instruction pointer back to a misaligned offset mid-execution — either
        /// truncating output silently or crashing on a garbage-decoded opcode.
        /// This program compiles to 600 bytes of straight-line code (50 print pairs at
        /// 12 bytes each), well past that former ceiling, and must run to completion.
        /// </summary>
        [Fact]
        public void LargeProgram_ExceedingFormerByteAddressCeiling_ExecutesWithoutCorruption()
        {
            const int count = 50;
            var source = new System.Text.StringBuilder();
            source.AppendLine("MOV AL, 0x01"); // write-char mode, set once
            for (int i = 0; i < count; i++)
            {
                source.AppendLine("MOV AH, 'X'");
                source.AppendLine("KEI 0x01");
            }
            source.AppendLine("KEI 0x02");

            CompileAndRun(source.ToString());

            Assert.Equal(new string('X', count) + "Halting!\n", _console.Output);
        }

        // ── Call stack / subroutines (CLL/RET) ──────────────────────────────────

        /// <summary>
        /// Regression test for the CallStack off-by-one bug: Call() wrote at stc_index
        /// then incremented; Return() read at stc_index (already one past the last
        /// write, since Call left it there) then decremented — so the first RET after
        /// any CLL returned to address 0 instead of the pushed return address. Calls
        /// the same subroutine TWICE: a fix that only handles a single CLL/RET pair
        /// (e.g. reading the right slot but never restoring it for reuse) would still
        /// pass a single-call test but fail this one.
        /// </summary>
        [Fact(Timeout = 5000)]
        public async Task CallReturn_SecondCallAfterReturn_ExecutesSubroutineTwice()
        {
            const string source = @"
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
";
            // Under the pre-fix bug this recurses into an infinite loop (RET always
            // jumps to address 0, which re-executes "JMP main" forever) rather than
            // throwing or returning wrong output — run off-thread so xUnit's Timeout
            // can actually kill it instead of hanging the whole test run.
            await Task.Run(() => CompileAndRun(source));
            Assert.Equal("AAHalting!\n", _console.Output);
        }

        /// <summary>
        /// Regression test for the call stack having no depth limit: recursing past
        /// the 255-slot call stack must raise a clean, controlled diagnostic (matching
        /// the house style established by RAM.SetByte's "attempted to overwrite its
        /// own code" guard) rather than corrupting state via an unchecked array write.
        /// </summary>
        [Fact(Timeout = 5000)]
        public async Task CallStack_ExceedingMaxDepth_ThrowsCleanDiagnostic()
        {
            const string source = @"
sub:
CLL sub
";
            var ex = await Assert.ThrowsAsync<Exception>(() => Task.Run(() => CompileAndRun(source)));
            Assert.Contains("call stack", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        // ── Register-indirect memory addressing (MOM/MOE) ───────────────────────

        /// <summary>
        /// MOM/MOE register-indirect addressing: the destination/source address is
        /// held in a register (X) at runtime rather than baked into the instruction
        /// as a literal. Stores a byte at the address currently in X, then loads it
        /// back through the same register.
        /// </summary>
        [Fact]
        public void IndirectMemoryAccess_StoreAndLoadThroughRegister()
        {
            const string source = @"
MOV X, 0x60
MOV AL, 'Z'
MOM AL, X
MOE AH, X
KEI 0x02
";
            VM vm = CompileAndRun(source);
            Assert.Equal((byte)'Z', vm.AH);
        }

        /// <summary>
        /// Proves the addressing is genuinely indirect (computed from the register's
        /// runtime value), not just a second flavor of immediate: the same register
        /// (X) is loaded with two DIFFERENT addresses in one run, and each store/load
        /// pair must hit its own distinct location without aliasing the other. A bug
        /// that used the register's ID byte as the address instead of its value would
        /// make both stores land on the same location and fail this.
        /// </summary>
        [Fact]
        public void IndirectMemoryAccess_TwoDifferentRegisterValues_AddressDistinctLocations()
        {
            const string source = @"
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
";
            VM vm = CompileAndRun(source);
            Assert.Equal((byte)'A', vm.AH);
            Assert.Equal((byte)'B', vm.BH);
        }

        // ── Byte stack (PSH/POP) overflow ───────────────────────────────────────

        /// <summary>
        /// Regression test for folding the byte-stack into the shared address space
        /// (previously an isolated, disconnected byte[256] buffer with no bounds
        /// checking at all): pushing enough values to walk SP down past RAMLimit must
        /// throw a clean diagnostic — the same guard RAM.SetByte already uses for
        /// direct writes — instead of silently corrupting the loaded program's code.
        /// Uses a small, explicit RAM size so a modest, non-looping number of PSH
        /// instructions is enough to cross the boundary; Globals.DefaultRamSize (1 MB)
        /// would need over a million pushes to reach the same effect.
        /// </summary>
        [Fact(Timeout = 5000)]
        public async Task StackPush_OverflowingIntoCode_ThrowsCleanDiagnostic()
        {
            var source = new System.Text.StringBuilder();
            source.AppendLine("MOV AL, 0x42");
            for (int i = 0; i < 100; i++)
                source.AppendLine("PSH AL");

            var ex = await Assert.ThrowsAsync<Exception>(() => Task.Run(() => CompileAndRun(source.ToString(), ramSize: 626)));
            Assert.Contains("overwrite its own code", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Regression test for POP with no corresponding PSH (stack underflow): must
        /// throw a clean diagnostic rather than reading past the top of RAM.
        /// </summary>
        [Fact(Timeout = 5000)]
        public async Task StackPop_WithEmptyStack_ThrowsCleanDiagnostic()
        {
            const string source = @"
POP AL
KEI 0x02
";
            var ex = await Assert.ThrowsAsync<Exception>(() => Task.Run(() => CompileAndRun(source)));
            Assert.Contains("stack", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
