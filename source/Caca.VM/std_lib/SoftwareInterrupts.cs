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

