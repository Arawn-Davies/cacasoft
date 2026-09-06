using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Caca.VM.StandardLib
{
    /// <summary>
    /// Implements the CIL kernel interrupt handler (KEI instruction, opcode 0x2B).
    ///
    /// <b>Usage in CIL source:</b>
    /// <code>
    ///   KEI 0x01   ; stdio I/O — behaviour selected by register AL
    ///   KEI 0x02   ; halt the VM
    /// </code>
    ///
    /// <b>KEI 0x01 — stdio (behaviour controlled by AL):</b>
    /// <list type="table">
    ///   <listheader><term>AL</term><description>Action</description></listheader>
    ///   <item><term>0x01</term><description>Write the character in AH to stdout.</description></item>
    ///   <item><term>0x02</term><description>Write a string from RAM: X = base address, B = byte count.</description></item>
    ///   <item><term>0x03</term><description>Read one character from stdin into AH.</description></item>
    ///   <item><term>0x04</term><description>Read a line from stdin into RAM at address X; B is set to the number of bytes written.</description></item>
    ///   <item><term>0x05</term><description>Write the 16-bit value of register B as a decimal integer to stdout.</description></item>
    ///   <item><term>0x06</term><description>Write the signed 32-bit value of register X as a decimal integer to stdout.</description></item>
    /// </list>
    ///
    /// <b>KEI 0x02 — halt:</b>
    /// Writes "Halting!" to stdout and stops VM execution.
    /// </summary>
    public static class KernelInterrupts
    {
        /// <summary>The VM instance that issued the KEI instruction.</summary>
        public static VM ParentVM;

        /// <summary>
        /// Dispatches a kernel interrupt.
        /// </summary>
        /// <param name="command">The interrupt number taken from the KEI operand (byte 1 of the instruction).</param>
        public static void HandleInterrupt(int command)
        {
            // ── KEI 0x01 — stdio ─────────────────────────────────────────────
            #region stdio
            if (command == 0x01)
            {
                if (Globals.DebugMode == true)
                {
                    Globals.console.Write("KEI 0x01: ");
                }

                if (ParentVM.AL == 0x01)
                {
                    // Write-character mode: output the single byte stored in AH as a char.
                    Globals.console.Write((char)ParentVM.AH);
                    if (Globals.DebugMode == true)
                        Globals.console.WriteLine(" 0x01");
                }
                else if (ParentVM.AL == 0x02)
                {
                    // Write-string mode: read B bytes from RAM starting at address X,
                    // convert each byte to a character and write them all to stdout.
                    byte[] toConvert = new byte[ParentVM.GetSplit('B')];
                    toConvert = ParentVM.ram.GetSection(ParentVM.X, ParentVM.GetSplit('B'));
                    string toPrint = "";
                    for (int i = 0; i < toConvert.Length; i++)
                    {
                        toPrint += (char)toConvert[i];
                    }
                    if (Globals.DebugMode == true)
                    {
                        Globals.console.WriteLine(" 0x02");
                    }
                    Globals.console.Write(toPrint);
                }
                else if (ParentVM.AL == 0x03)
                {
                    // Read-character mode: read one character from stdin and store it in AH.
                    ParentVM.AH = (byte)Globals.console.Read();
                }
                else if (ParentVM.AL == 0x04)
                {
                    // Read-line mode: read a line of text from stdin, store the raw bytes in
                    // RAM at address X, and set register B to the number of bytes written.
                    string toConvert = Globals.console.ReadLine();
                    byte[] toWrite = new byte[toConvert.Length];
                    for (int i = 0; i < toWrite.Length; i++)
                    {
                        toWrite[i] = (byte)toConvert[i];
                    }
                    ParentVM.SetSplit('B', toWrite.Length);
                    ParentVM.ram.SetSection(ParentVM.X, toWrite);
                }
                else if (ParentVM.AL == 0x05)
                {
                    // Write-integer mode: print the 16-bit value in register B as a
                    // decimal (base-10) ASCII string.  Useful for printing numeric results
                    // without manually converting digits. Unsigned, and 16 bits wide --
                    // see 0x06 for a full-range signed 32-bit equivalent.
                    Globals.console.Write(ParentVM.GetSplit('B').ToString());
                }
                else if (ParentVM.AL == 0x06)
                {
                    // Write-signed-integer mode: print the signed 32-bit value in
                    // register X as a decimal (base-10) ASCII string, sign included.
                    // int.ToString() already handles every edge case correctly,
                    // including int.MinValue (negating it would overflow; ToString()
                    // never negates, it formats the two's complement value directly).
                    Globals.console.Write(ParentVM.X.ToString());
                }
            }
            // ── KEI 0x02 — halt ──────────────────────────────────────────────
            else if (command == 0x02)
            {
                if (Globals.DebugMode == true)
                {
                    Globals.console.Write("KEI 0x02: ");
                }
                // Print the standard halt message and stop the execution loop.
                Globals.console.WriteLine("Halting!");
                ParentVM.Halt();
            }
            else
            {
                // Unknown interrupt number — halt to protect program state.
                Globals.console.WriteLine("Undocumented function: " + command + "\nHalting for protection of data");
                ParentVM.Halt();
            }
            #endregion
        }
    }
}