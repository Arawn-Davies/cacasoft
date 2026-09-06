using System;
using Caca.VM.StandardLib;
using Caca.VM.Conversions;

namespace Caca.VM
{
    public partial class VM
    {
        /// <summary>
        /// Reads a little-endian 32-bit integer from RAM starting at startingIndex.
        /// </summary>
        /// <param name="startingIndex">Starting byte offset in RAM</param>
        /// <returns>32-bit integer value</returns>
        private int Get32BitParameter(int startingIndex)
        {
            return ram.memory[startingIndex]
                 | (ram.memory[startingIndex + 1] << 8)
                 | (ram.memory[startingIndex + 2] << 16)
                 | (ram.memory[startingIndex + 3] << 24);
        }

        /// <summary>
        /// Writes <paramref name="Content"/> into the register identified by <paramref name="Register"/>.
        /// PC, IP and SS are full-width, like X/Y, so they can address the entire RAM segment.
        /// 8-bit registers (AL, AH, BL, BH, CL, CH) are truncated to their low byte.
        /// Writing to the composite registers A (0xF4), B (0xF7), or C (0xFA) splits the 16-bit value
        /// across the corresponding L/H byte pair via <see cref="SetSplit"/>.
        /// Writing to SP (0xF2) is silently ignored — SP is managed automatically by stack operations
        /// (PSH/POP) and must not be modified by program code.
        /// </summary>
        /// <param name="Register">Register identifier byte (0xF0–0xFE).</param>
        /// <param name="Content">Value to write.</param>
        private void SetRegister(byte Register, int Content)
        {
            if (Register == (byte)0xF0)
                PC = Content;
            else if (Register == (byte)0xF1)
                IP = Content;
            //0xF2 (Stack Pointer) is read only
            else if (Register == (byte)0xF3)
                SS = Content;
            else if (Register == (byte)0xF4)
                SetSplit('A', Content);
            else if (Register == (byte)0xF5)
                AL = (byte)Content;
            else if (Register == (byte)0xF6)
                AH = (byte)Content;
            else if (Register == (byte)0xF7)
                SetSplit('B', Content);
            else if (Register == (byte)0xF8)
                BL = (byte)Content;
            else if (Register == (byte)0xF9)
                BH = (byte)Content;
            else if (Register == (byte)0xFA)
                SetSplit('C', Content);
            else if (Register == (byte)0xFB)
                CL = (byte)Content;
            else if (Register == (byte)0xFC)
                CH = (byte)Content;
            else if (Register == (byte)0xFD)
                X = Content;
            else if (Register == (byte)0xFE)
                Y = Content;
            else
                throw new Exception("ERROR: The register " + Register + " is not a register.");
        }

        /// <summary>
        /// Reads the current value of the register identified by <paramref name="Register"/>.
        /// Composite registers A (0xF4), B (0xF7), and C (0xFA) return their combined 16-bit value
        /// via <see cref="GetSplit"/>. Unknown register bytes return 0.
        /// </summary>
        /// <param name="Register">Register identifier byte (0xF0–0xFE).</param>
        /// <returns>Current integer value stored in the register.</returns>
        private int GetRegister(byte Register)
        {
            if (Register == (byte)0xF0)
                return PC;
            else if (Register == (byte)0xF1)
                return IP;
            else if (Register == (byte)0xF2)
                return SP;
            else if (Register == (byte)0xF3)
                return SS;
            else if (Register == (byte)0xF4)
                return GetSplit('A');
            else if (Register == (byte)0xF5)
                return AL;
            else if (Register == (byte)0xF6)
                return AH;
            else if (Register == (byte)0xF7)
                return GetSplit('B');
            else if (Register == (byte)0xF8)
                return BL;
            else if (Register == (byte)0xF9)
                return BH;
            else if (Register == (byte)0xFA)
                return GetSplit('C');
            else if (Register == (byte)0xFB)
                return CL;
            else if (Register == (byte)0xFC)
                return CH;
            else if (Register == (byte)0xFD)
                return X;
            else if (Register == (byte)0xFE)
                return Y;
            else
                return 0;
        }
    }
}