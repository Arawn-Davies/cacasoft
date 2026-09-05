namespace Caca.VM.Decompiler
{
    /// <summary>Maps CacaVM opcode bytes to their mnemonic strings.</summary>
    internal static class Instruction
    {
        /// <summary>
        /// Returns the assembly mnemonic for the given opcode byte
        /// (e.g. <c>0x01</c> → <c>"MOV"</c>).
        /// Returns an empty string for unrecognised opcodes.
        /// </summary>
        public static string GetName(byte opcode) => opcode switch
        {
            // Register / memory
            0x01 => "MOV", 0x3A => "MOM", 0x3B => "MOE", 0x02 => "SWP",
            // Test
            0x1A => "TEQ", 0x1B => "TNE", 0x1C => "TLT", 0x1D => "TMT",
            // Arithmetic
            0x04 => "ADD", 0x05 => "SUB", 0x08 => "INC", 0x09 => "DEC",
            0x30 => "MUL", 0x31 => "DIV",
            // Bitwise
            0x06 => "SHL", 0x07 => "SHR", 0x0E => "ROL", 0x0F => "ROR",
            0x0A => "AND", 0x0B => "BOR", 0x0C => "XOR", 0x0D => "NOT",
            // Flow
            0x10 => "JMP", 0x11 => "CLL", 0x12 => "RET",
            0x13 => "JMT", 0x14 => "JMF", 0x17 => "CLT", 0x18 => "CLF",
            // Stack
            0x20 => "PSH", 0x21 => "POP",
            // I/O
            0x24 => "INB", 0x25 => "INW", 0x26 => "IND",
            0x27 => "OUB", 0x28 => "OUW", 0x29 => "OUD",
            // Interrupts
            0x2A => "SWI", 0x2B => "KEI",
            _    => "",
        };
    }
}
