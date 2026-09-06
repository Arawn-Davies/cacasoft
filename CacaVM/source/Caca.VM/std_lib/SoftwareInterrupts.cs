using System;
using System.Collections.Generic;
using System.Text;

namespace Caca.VM.StandardLib
{
    /// <summary>
    /// Implements the CIL software interrupt handler (SWI instruction, opcode 0x2A).
    ///
    /// <b>Usage in CIL source:</b>
    /// <code>
    ///   SWI 0x01   ; string utilities — operation selected by register AL
    /// </code>
    ///
    /// <b>SWI 0x01 — string utilities (behaviour controlled by AL):</b>
    /// <list type="table">
    ///   <listheader><term>AL</term><description>Action</description></listheader>
    ///   <item><term>0x01</term><description>Strlen: count bytes at address X until a zero byte; store length in B.</description></item>
    ///   <item><term>0x02</term><description>Strcpy: copy B bytes from address X to address Y.</description></item>
    ///   <item><term>0x03</term><description>Strcmp: compare null-terminated strings at addresses X and Y for equality; store the result (1 equal, 0 not) in B.</description></item>
    ///   <item><term>0x04</term><description>Atoi: parse a signed decimal integer from the null-terminated string at address X; store the result in Y.</description></item>
    /// </list>
    /// </summary>
    public static class SoftwareInterrupts
    {
        /// <summary>The VM instance that issued the SWI instruction.</summary>
        public static VM ParentVM;

        /// <summary>
        /// Dispatches a software interrupt.
        /// </summary>
        /// <param name="command">The interrupt number taken from the SWI operand (byte 1 of the instruction).</param>
        public static void HandleInterrupt(int command)
        {
            // SWI 0x01 — String utilities
            // AL selects operation; registers X, Y, B used for address/length.
            if (command == 0x01)
            {
                if (ParentVM.AL == 0x01)
                {
                    // Strlen: count bytes at address X until a zero byte; store length in B.
                    int addr = ParentVM.X;
                    int limit = ParentVM.ram.memory.Length;
                    int len = 0;
                    while (addr + len < limit && ParentVM.ram.memory[addr + len] != 0x00)
                        len++;
                    ParentVM.SetSplit('B', len);
                }
                else if (ParentVM.AL == 0x02)
                {
                    // Strcpy: copy B bytes from address X to address Y.
                    int src = ParentVM.X;
                    int dst = ParentVM.Y;
                    int count = ParentVM.GetSplit('B');
                    int limit = ParentVM.ram.memory.Length;
                    if (src < 0 || src + count > limit || dst < 0 || dst + count > limit)
                    {
                        Globals.console.WriteLine("SWI 0x01: strcpy address out of range\nHalting for protection of data");
                        ParentVM.Halt();
                        return;
                    }
                    byte[] data = ParentVM.ram.GetSection(src, count);
                    ParentVM.ram.SetSection(dst, data);
                }
                else if (ParentVM.AL == 0x03)
                {
                    // Strcmp: compare null-terminated strings at addresses X and Y.
                    // Equality only -- not lexicographic ordering. Ordering would need
                    // a signed result, and B (the 16-bit BL/BH composite every other
                    // string routine here uses) is combined unsigned (see
                    // BitOps.CombineBytes): there is no sign bit to carry a "less than
                    // zero" result through it correctly. B = 1 if equal, 0 if not,
                    // matching strlen's convention of a plain count/flag in B.
                    int limit = ParentVM.ram.memory.Length;
                    int xAddr = ParentVM.X;
                    int yAddr = ParentVM.Y;
                    bool equal = true;
                    int i = 0;
                    while (true)
                    {
                        if (xAddr + i < 0 || xAddr + i >= limit || yAddr + i < 0 || yAddr + i >= limit)
                        {
                            Globals.console.WriteLine("SWI 0x01: strcmp address out of range\nHalting for protection of data");
                            ParentVM.Halt();
                            return;
                        }

                        byte xByte = ParentVM.ram.memory[xAddr + i];
                        byte yByte = ParentVM.ram.memory[yAddr + i];

                        if (xByte != yByte)
                        {
                            equal = false;
                            break;
                        }

                        if (xByte == 0x00)
                        {
                            // Both strings terminated at the same position with every
                            // byte equal so far: they match.
                            break;
                        }

                        i++;
                    }

                    ParentVM.SetSplit('B', equal ? 1 : 0);
                }
                else if (ParentVM.AL == 0x04)
                {
                    // Atoi: parse a signed decimal integer from the null-terminated
                    // string at address X into Y. An optional leading '+' or '-' is
                    // read first, then one or more ASCII digits; parsing stops at the
                    // first non-digit or the terminator. No valid digits at all (an
                    // empty string, or one starting with neither a sign nor a digit)
                    // is defined, not undefined, behaviour: Y = 0, the same convention
                    // C's own atoi uses for unparseable input.
                    int limit = ParentVM.ram.memory.Length;
                    int addr = ParentVM.X;

                    if (addr < 0 || addr >= limit)
                    {
                        Globals.console.WriteLine("SWI 0x01: atoi address out of range\nHalting for protection of data");
                        ParentVM.Halt();
                        return;
                    }

                    bool negative = false;
                    byte first = ParentVM.ram.memory[addr];

                    if (first == (byte)'-' || first == (byte)'+')
                    {
                        negative = first == (byte)'-';
                        addr++;
                    }

                    int result = 0;
                    bool sawDigit = false;

                    while (addr < limit)
                    {
                        byte b = ParentVM.ram.memory[addr];

                        if (b < (byte)'0' || b > (byte)'9')
                        {
                            break;
                        }

                        // Unchecked, like every other arithmetic instruction this VM
                        // has: a value too large to fit wraps rather than throwing.
                        result = (result * 10) + (b - (byte)'0');
                        sawDigit = true;
                        addr++;
                    }

                    ParentVM.Y = sawDigit ? (negative ? -result : result) : 0;
                }
                else
                {
                    Globals.console.WriteLine("SWI 0x01: unknown mode 0x" + ParentVM.AL.ToString("X2") + "\nHalting for protection of data");
                    ParentVM.Halt();
                }
            }
            else
            {
                Globals.console.WriteLine("Undocumented SWI: " + command + "\nHalting for protection of data");
                ParentVM.Halt();
            }
        }
    }
}

