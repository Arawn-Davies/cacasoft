using System;
using System.Collections.Generic;

namespace Caca.VM.Compiler
{
    /// <summary>
    /// Resolves mnemonic strings to CacaVM opcode bytes and encodes
    /// 6-byte instructions using the CIL encoding:
    ///   byte 0 = (opcode &lt;&lt; 2) | addrmode
    ///   byte 1 = param1  (8-bit)
    ///   bytes 2-5 = param2 (32-bit little-endian)
    /// </summary>
    internal static class Instruction
    {
        private static readonly Dictionary<string, byte> _opcodes =
            new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            // Register / memory
            { "MOV", 0x01 }, { "MOM", 0x3A }, { "MOE", 0x3B }, { "SWP", 0x02 },
            // Test
            { "TEQ", 0x1A }, { "TNE", 0x1B }, { "TLT", 0x1C }, { "TMT", 0x1D },
            // Arithmetic
            { "ADD", 0x04 }, { "SUB", 0x05 }, { "INC", 0x08 }, { "DEC", 0x09 },
            { "MUL", 0x30 }, { "DIV", 0x31 },
            // Bitwise
            { "SHL", 0x06 }, { "SHR", 0x07 }, { "ROL", 0x0E }, { "ROR", 0x0F },
            { "AND", 0x0A }, { "BOR", 0x0B }, { "XOR", 0x0C }, { "NOT", 0x0D },
            // Flow
            { "JMP", 0x10 }, { "CLL", 0x11 }, { "RET", 0x12 },
            { "JMT", 0x13 }, { "JMF", 0x14 }, { "CLT", 0x17 }, { "CLF", 0x18 },
            // Stack
            { "PSH", 0x20 }, { "POP", 0x21 },
            // I/O
            { "INB", 0x24 }, { "INW", 0x25 }, { "IND", 0x26 },
            { "OUB", 0x27 }, { "OUW", 0x28 }, { "OUD", 0x29 },
            // Interrupts
            { "SWI", 0x2A }, { "KEI", 0x2B },
        };

        /// <summary>Returns true when <paramref name="token"/> is a known mnemonic.</summary>
        public static bool IsMnemonic(string token) => _opcodes.ContainsKey(token);

        /// <summary>Returns the opcode byte for a mnemonic, or 0 if unknown.</summary>
        public static byte GetOpcode(string mnemonic) =>
            _opcodes.TryGetValue(mnemonic, out byte b) ? b : (byte)0;

        /// <summary>
        /// Encodes a single 6-byte CacaVM instruction.
        /// </summary>
        public static byte[] Encode(byte opcode, AddressMode mode, byte param1, int param2)
        {
            byte[] instr = new byte[6];
            instr[0] = (byte)((opcode << 2) | (int)mode);
            instr[1] = param1;
            byte[] p2 = BitConverter.GetBytes(param2);
            instr[2] = p2[0];
            instr[3] = p2[1];
            instr[4] = p2[2];
            instr[5] = p2[3];
            return instr;
        }
    }
}
