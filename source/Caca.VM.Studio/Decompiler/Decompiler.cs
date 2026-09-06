using System;
using System.Text;

namespace Caca.VM.Decompiler
{
    /// <summary>
    /// Decodes a CacaVM binary (raw bytecode or .ilc) back to assembly text.
    ///
    /// Fixes over the original Lilac decompiler:
    ///   • correct loop structure (each instruction decoded exactly once)
    ///   • proper little-endian 32-bit param2 reconstruction
    ///   • .ilc header stripping
    ///   • per-instruction operand formatting
    /// </summary>
    public class Decompiler
    {
        private static readonly byte[] IlcMagic = { 0x43, 0x49, 0x4C, 0x00 };

        private readonly byte[] _executable;

        public Decompiler(byte[] executable)
        {
            _executable = executable ?? throw new ArgumentNullException(nameof(executable));
        }

        /// <summary>Decodes the binary and returns assembly source text.</summary>
        public string Decompile()
        {
            if (_executable.Length == 0)
                throw new Exception("Executable cannot be empty.");

            byte[] code = ExtractCode(_executable);
            var sb = new StringBuilder();
            sb.AppendLine("; Decompiled by Caca Studio");
            sb.AppendLine();

            for (int i = 0; i + 6 <= code.Length; i += 6)
            {
                byte b0     = code[i];
                byte opcode = (byte)((b0 >> 2) & 0x3F);
                byte addrMode = (byte)(b0 & 0x03);

                if (opcode == 0x00)
                    break; // null opcode = end of program

                byte p1b  = code[i + 1];
                byte p2b0 = code[i + 2]; // low byte of param2 (register byte in RegReg mode)
                int  p2   = code[i + 2]
                          | (code[i + 3] << 8)
                          | (code[i + 4] << 16)
                          | (code[i + 5] << 24);

                string name = Instruction.GetName(opcode);
                if (string.IsNullOrEmpty(name))
                {
                    sb.AppendLine($"; unknown opcode 0x{opcode:X2} at offset {i}");
                    continue;
                }

                sb.AppendLine(Format(name, opcode, (AddressMode)addrMode, p1b, p2b0, p2));
            }

            return sb.ToString();
        }

        // ── Instruction formatting ────────────────────────────────────────────

        private static string Format(string name, byte opcode, AddressMode mode,
                                     byte p1b, byte p2b0, int p2)
        {
            switch (opcode)
            {
                // No operands
                case 0x12: // RET
                    return name;

                // Single register operand
                case 0x08: // INC
                case 0x09: // DEC
                case 0x0D: // NOT
                case 0x21: // POP
                    return $"{name} {Registers.GetName(p1b)}";

                // Single operand – register or value (PSH, JMP, CLL, JMT, JMF, CLT, CLF)
                case 0x20: // PSH
                case 0x10: // JMP
                case 0x11: // CLL
                case 0x13: // JMT
                case 0x14: // JMF
                case 0x17: // CLT
                case 0x18: // CLF
                    if (mode == AddressMode.RegisterRegister || mode == AddressMode.RegisterValue)
                        return $"{name} {Registers.GetName(p1b)}";
                    return $"{name} 0x{(byte)p2:X2}";

                // Single value operand (interrupt commands stored in p1b)
                case 0x2A: // SWI
                case 0x2B: // KEI
                    return $"{name} 0x{p1b:X2}";

                // Single operand – register or a FULL 32-bit value (PSHN, POPN):
                // deliberately not sharing the PSH/JMP/etc. bucket above, whose
                // "0x{(byte)p2:X2}" truncates to a single byte — fine for PSH's
                // own literal (a byte it pushes), wrong for a byte COUNT that
                // routinely exceeds 255 (e.g. 256 for read_int's scratch buffer).
                case 0x22: // PSHN
                case 0x23: // POPN
                    if (mode == AddressMode.RegisterRegister || mode == AddressMode.RegisterValue)
                        return $"{name} {Registers.GetName(p1b)}";
                    return $"{name} 0x{p2:X}";

                // Two register operands
                case 0x02: // SWP
                case 0x1A: // TEQ
                case 0x1B: // TNE
                case 0x1C: // TLT
                case 0x1D: // TMT
                    return $"{name} {Registers.GetName(p1b)}, {Registers.GetName(p2b0)}";

                // Shift / rotate: reg, value
                case 0x06: // SHL
                case 0x07: // SHR
                case 0x0E: // ROL
                case 0x0F: // ROR
                    return $"{name} {Registers.GetName(p1b)}, {p2}";

                // Memory write: MOM src, addr
                case 0x3A:
                {
                    string src = Registers.IsRegister(p1b)
                        ? Registers.GetName(p1b)
                        : $"0x{p1b:X2}";
                    return $"{name} {src}, 0x{p2:X4}";
                }

                // Memory read: MOE dest, addr
                case 0x3B:
                    return $"{name} {Registers.GetName(p1b)}, 0x{p2:X4}";

                // I/O stubs: port, reg
                case 0x24: case 0x25: case 0x26: // INB INW IND
                case 0x27: case 0x28: case 0x29: // OUB OUW OUD
                {
                    string reg = Registers.GetName(p2b0);
                    string p2str = string.IsNullOrEmpty(reg) ? $"0x{p2b0:X2}" : reg;
                    return $"{name} 0x{p1b:X2}, {p2str}";
                }

                // MOV, ADD, SUB, MUL, DIV, AND, BOR, XOR – reg, reg|val
                default:
                {
                    string p2str = mode == AddressMode.RegisterRegister
                        ? Registers.GetName(p2b0)
                        : $"0x{p2:X2}";
                    return $"{name} {Registers.GetName(p1b)}, {p2str}";
                }
            }
        }

        // ── .ilc header stripping ─────────────────────────────────────────────

        private static byte[] ExtractCode(byte[] data)
        {
            if (data.Length < 8) return data;

            for (int i = 0; i < IlcMagic.Length; i++)
                if (data[i] != IlcMagic[i]) return data; // no header — treat as raw

            ushort sectionCount = (ushort)(data[6] | (data[7] << 8));
            int offset = 8;
            for (int s = 0; s < sectionCount; s++)
            {
                if (offset + 6 > data.Length) break;
                ushort type = (ushort)(data[offset] | (data[offset + 1] << 8));
                int    len  = data[offset + 2]
                            | (data[offset + 3] << 8)
                            | (data[offset + 4] << 16)
                            | (data[offset + 5] << 24);
                offset += 6;
                if (type == 0x0001) // code section
                {
                    byte[] code = new byte[len];
                    Array.Copy(data, offset, code, 0, len);
                    return code;
                }
                offset += len;
            }
            return data; // fallback
        }
    }
}
