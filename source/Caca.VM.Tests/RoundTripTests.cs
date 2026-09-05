using System;
using System.Linq;
using Xunit;
using Caca.VM;
using Caca.VM.Compiler;
using Caca.VM.Decompiler;

namespace Caca.VM.Tests
{
    /// <summary>
    /// Round-trip tests: compile CIL assembly → decompile back to assembly → recompile.
    /// Two forms of equivalence are checked:
    ///
    ///   1. <b>Bytecode equality</b> – for programs that use only instructions whose
    ///      decompiler output re-encodes with the same addressing mode, the two byte
    ///      arrays must be identical.
    ///
    ///   2. <b>Functional equality</b> – for programs that include interrupts (where
    ///      the decompiler may emit a different addressing-mode byte that is
    ///      functionally equivalent), we execute both compilations and compare their
    ///      console output.
    /// </summary>
    [Collection("VM")]
    public sealed class RoundTripTests
    {
        private readonly TestConsole _console = new TestConsole();

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static byte[] Compile(string source) => new global::Caca.VM.Compiler.Compiler(source).Compile();

        private static string Decompile(byte[] code) => new global::Caca.VM.Decompiler.Decompiler(code).Decompile();

        private string Execute(byte[] code)
        {
            Caca.VM.Globals.console = _console;
            Caca.VM.Globals.DebugMode = false;
            _console.Reset();
            new VM(code, Globals.DefaultRamSize).Execute();
            return _console.Output;
        }

        // ── Bytecode round-trip ──────────────────────────────────────────────────

        /// <summary>
        /// A program that only uses MOV (register↔register and register↔value)
        /// must survive compile → decompile → recompile with bit-identical bytecode.
        /// These instructions have stable decompiler output: decompiled hex literals
        /// and register names re-assemble with the same addressing mode.
        /// </summary>
        [Fact]
        public void MovOnly_BytecodeRoundTripIsIdentical()
        {
            const string source = @"
MOV AL, 42
MOV AH, AL
MOV BL, 0xFF
";
            byte[] first   = Compile(source);
            string asm2    = Decompile(first);
            byte[] second  = Compile(asm2);

            Assert.Equal(first, second);
        }

        /// <summary>
        /// Arithmetic instructions (ADD, SUB, MUL, INC, DEC) round-trip cleanly
        /// because the decompiler outputs hex literals that re-assemble identically.
        /// </summary>
        [Fact]
        public void ArithmeticInstructions_BytecodeRoundTripIsIdentical()
        {
            const string source = @"
MOV AL, 10
ADD AL, 5
SUB AL, 3
INC AL
DEC AL
MUL AL, 2
";
            byte[] first  = Compile(source);
            string asm2   = Decompile(first);
            byte[] second = Compile(asm2);

            Assert.Equal(first, second);
        }

        /// <summary>
        /// Bitwise instructions round-trip cleanly.
        /// </summary>
        [Fact]
        public void BitwiseInstructions_BytecodeRoundTripIsIdentical()
        {
            const string source = @"
MOV AL, 0xFF
AND AL, 0x0F
BOR AL, 0xF0
XOR AL, 0xAA
SHL AL, 1
SHR AL, 1
";
            byte[] first  = Compile(source);
            string asm2   = Decompile(first);
            byte[] second = Compile(asm2);

            Assert.Equal(first, second);
        }

        // ── Functional round-trip ────────────────────────────────────────────────

        /// <summary>
        /// HelloWorld: compile the assembly source, then decompile and recompile,
        /// then execute both versions and assert they produce identical console output.
        /// The two bytecodes may differ in the addressing-mode bits of interrupt
        /// instructions (KEI), but the VM ignores those bits for interrupts, so the
        /// programs are functionally equivalent.
        /// </summary>
        [Fact]
        public void HelloWorld_FunctionalRoundTripOutputMatches()
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
            byte[] original   = Compile(source);
            string decompiled = Decompile(original);
            byte[] roundTrip  = Compile(decompiled);

            string output1 = Execute(original);
            string output2 = Execute(roundTrip);

            Assert.Equal(output1, output2);
            Assert.Equal("Hello, World!\nHalting!\n", output1);
        }

        /// <summary>
        /// A program with conditional jumps: after round-trip the execution behaviour
        /// (register state, output) must be identical to the original.
        /// </summary>
        [Fact]
        public void ConditionalJump_FunctionalRoundTripOutputMatches()
        {
            const string source = @"
MOV AL, 0x01
MOV AH, 0x01
TEQ AL, AH
JMT equal
MOV AH, 'N'
KEI 0x01
equal:
MOV AH, 'Y'
KEI 0x01
KEI 0x02
";
            byte[] original   = Compile(source);
            string decompiled = Decompile(original);
            byte[] roundTrip  = Compile(decompiled);

            string output1 = Execute(original);
            string output2 = Execute(roundTrip);

            Assert.Equal(output1, output2);
            // AL == AH so the JMT fires, skipping 'N', printing only 'Y' then halt.
            Assert.Equal("YHalting!\n", output1);
        }

        /// <summary>
        /// Decompiled output must be non-empty and contain the standard header comment.
        /// </summary>
        [Fact]
        public void Decompiler_IncludesHeaderComment()
        {
            byte[] code = Compile("MOV AL, 1\nKEI 0x02");
            string asm  = Decompile(code);
            Assert.Contains("; Decompiled by Caca Studio", asm);
        }

        /// <summary>
        /// Decompiled output must contain recognisable mnemonics for the instructions
        /// that were originally assembled.
        /// </summary>
        [Fact]
        public void Decompiler_ContainsMnemonics()
        {
            byte[] code = Compile("MOV AL, 1\nADD AL, 2\nKEI 0x02");
            string asm  = Decompile(code);
            Assert.Contains("MOV", asm);
            Assert.Contains("ADD", asm);
            Assert.Contains("KEI", asm);
        }
    }
}
