using System;
using Xunit;
using Caca.VM.Compiler;

namespace Caca.VM.Tests
{
    /// <summary>
    /// Unit tests for the two-pass assembler.  These tests verify that known CIL
    /// assembly source produces the expected raw bytecode — independent of VM
    /// execution — so that encoding regressions are caught at the compiler level.
    ///
    /// Instruction encoding (spec §2):
    ///   byte 0  = (opcode &lt;&lt; 2) | address_mode
    ///   byte 1  = param1 (8-bit)
    ///   bytes 2-5 = param2 (32-bit little-endian)
    ///
    /// Opcodes: MOV = 0x01, ADD = 0x04, SUB = 0x05, KEI = 0x2B
    /// Modes:   RegReg = 0x00, ValReg = 0x01, RegVal = 0x02, ValVal = 0x03
    /// Registers: AL = 0xF5, AH = 0xF6
    /// </summary>
    public sealed class CompilerTests
    {
        private static byte[] Compile(string source) => new global::Caca.VM.Compiler.Compiler(source).Compile();

        // ── Single-instruction bytecode checks ──────────────────────────────────

        /// <summary>
        /// MOV AL, 0x01 — RegVal mode (dest register, src immediate)
        ///   byte 0 = (0x01 &lt;&lt; 2) | 0x02 = 0x06
        ///   byte 1 = 0xF5  (AL)
        ///   bytes 2-5 = 0x01 0x00 0x00 0x00
        /// </summary>
        [Fact]
        public void MovRegVal_EncodesCorrectly()
        {
            byte[] code = Compile("MOV AL, 0x01");
            Assert.Equal(6, code.Length);
            Assert.Equal(0x06, code[0]); // (MOV=0x01 << 2) | RegVal=0x02
            Assert.Equal(0xF5, code[1]); // AL
            Assert.Equal(0x01, code[2]); // param2 low byte
            Assert.Equal(0x00, code[3]);
            Assert.Equal(0x00, code[4]);
            Assert.Equal(0x00, code[5]);
        }

        /// <summary>
        /// MOV AH, AL — RegReg mode
        ///   byte 0 = (0x01 &lt;&lt; 2) | 0x00 = 0x04
        ///   byte 1 = 0xF6  (AH)
        ///   bytes 2-5: low byte = 0xF5 (AL), rest zeros
        /// </summary>
        [Fact]
        public void MovRegReg_EncodesCorrectly()
        {
            byte[] code = Compile("MOV AH, AL");
            Assert.Equal(6, code.Length);
            Assert.Equal(0x04, code[0]); // (MOV=0x01 << 2) | RegReg=0x00
            Assert.Equal(0xF6, code[1]); // AH
            Assert.Equal(0xF5, code[2]); // AL in low byte of param2
        }

        /// <summary>
        /// KEI 0x01 — single value operand; param1 carries the interrupt number.
        ///   opcode = 0x2B
        ///   byte 1 = 0x01
        /// </summary>
        [Fact]
        public void Kei_EncodesInterruptNumber()
        {
            byte[] code = Compile("KEI 0x01");
            Assert.Equal(6, code.Length);
            // Upper 6 bits of byte 0 must be the KEI opcode
            int decodedOpcode = (code[0] >> 2) & 0x3F;
            Assert.Equal(0x2B, decodedOpcode);
            Assert.Equal(0x01, code[1]); // interrupt number in param1
        }

        /// <summary>
        /// ADD AL, 5 — RegVal mode
        ///   byte 0 = (0x04 &lt;&lt; 2) | 0x02 = 0x12
        /// </summary>
        [Fact]
        public void AddRegVal_EncodesCorrectly()
        {
            byte[] code = Compile("ADD AL, 5");
            Assert.Equal(6, code.Length);
            Assert.Equal(0x12, code[0]); // (ADD=0x04 << 2) | RegVal=0x02
            Assert.Equal(0xF5, code[1]); // AL
            Assert.Equal(0x05, code[2]); // 5 in low byte of param2
        }

        /// <summary>
        /// Char literals: 'H' = 0x48.  The compiler must parse single-quoted
        /// characters and emit the correct byte value.
        /// </summary>
        [Fact]
        public void CharLiteral_EncodesCorrectByte()
        {
            byte[] code = Compile("MOV AH, 'H'");
            Assert.Equal(0x48, code[2]); // 'H' = 0x48
        }

        /// <summary>
        /// Escape sequence '\n' = 0x0A must be handled inside char literals.
        /// </summary>
        [Fact]
        public void EscapeNewline_EncodesCorrectByte()
        {
            byte[] code = Compile(@"MOV AH, '\n'");
            Assert.Equal(0x0A, code[2]); // '\n' = 0x0A
        }

        // ── Multi-instruction programs ───────────────────────────────────────────

        /// <summary>
        /// A two-instruction program (MOV + KEI) must produce exactly 12 bytes.
        /// </summary>
        [Fact]
        public void TwoInstructions_ProducesTwelveBytes()
        {
            byte[] code = Compile("MOV AL, 1\nKEI 0x02");
            Assert.Equal(12, code.Length);
        }

        /// <summary>
        /// Label resolution: a JMP to a forward label must encode the target byte
        /// offset (= instruction index × 6) in param2.
        /// JMP target  — 1 instruction (6 bytes), target: is the second instruction
        /// at byte offset 6.
        /// </summary>
        [Fact]
        public void LabelResolution_EncodesCorrectOffset()
        {
            const string source = @"
JMP target
MOV AL, 0xFF
target:
KEI 0x02
";
            byte[] code = Compile(source);

            // JMP is instruction 0 (bytes 0-5).  Its param2 must equal 12 (byte
            // offset of 'target', which is the third instruction: 2 × 6 = 12).
            int jumpTarget = code[2]
                           | (code[3] << 8)
                           | (code[4] << 16)
                           | (code[5] << 24);
            Assert.Equal(12, jumpTarget);
        }

        // ── Comment / whitespace stripping ──────────────────────────────────────

        /// <summary>
        /// Lines that are pure comments or blank must not generate any bytecode.
        /// </summary>
        [Fact]
        public void CommentsAndBlanks_DoNotGenerateBytecode()
        {
            const string source = @"
; this is a comment
// another comment

MOV AL, 1
";
            byte[] code = Compile(source);
            Assert.Equal(6, code.Length);
        }

        // ── DB pseudo-instruction ────────────────────────────────────────────────

        /// <summary>
        /// DB with a string literal emits the correct raw bytes.
        /// <c>DB "Hi", 0x0A, 0x00</c> must produce exactly 4 bytes: 0x48 0x69 0x0A 0x00.
        /// </summary>
        [Fact]
        public void Db_StringLiteral_EmitsRawBytes()
        {
            byte[] code = Compile(@"DB ""Hi"", 0x0A, 0x00");
            Assert.Equal(4, code.Length);
            Assert.Equal(0x48, code[0]); // 'H'
            Assert.Equal(0x69, code[1]); // 'i'
            Assert.Equal(0x0A, code[2]); // newline
            Assert.Equal(0x00, code[3]); // null terminator
        }

        /// <summary>
        /// A label pointing to a DB section must resolve to the correct byte offset.
        /// Layout: JMP main (6 bytes) | hello: DB "AB" (2 bytes) | main: KEI 0x02 (6 bytes).
        /// The JMP target (main) must be at byte offset 8.
        /// </summary>
        [Fact]
        public void Db_LabelAfterDb_ResolvesToCorrectOffset()
        {
            const string source = @"
JMP main
hello:
DB ""AB""
main:
KEI 0x02
";
            byte[] code = Compile(source);

            // JMP is bytes 0-5.  Its param2 must equal 8 (offset of main = 6+2).
            int jumpTarget = code[2]
                           | (code[3] << 8)
                           | (code[4] << 16)
                           | (code[5] << 24);
            Assert.Equal(8, jumpTarget);
        }

        /// <summary>
        /// DB with char literals emits the correct bytes.
        /// </summary>
        [Fact]
        public void Db_CharLiterals_EmitsRawBytes()
        {
            byte[] code = Compile(@"DB 'A', '\n', 0x00");
            Assert.Equal(3, code.Length);
            Assert.Equal(0x41, code[0]); // 'A'
            Assert.Equal(0x0A, code[1]); // '\n'
            Assert.Equal(0x00, code[2]); // null
        }
    }
}
