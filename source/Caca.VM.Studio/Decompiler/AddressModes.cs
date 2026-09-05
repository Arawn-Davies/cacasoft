namespace Caca.VM.Decompiler
{
    /// <summary>
    /// Addressing modes for CacaVM instructions, mirroring the 2-bit field in
    /// byte 0 of each encoded instruction (bits 1–0). See spec §2.1.
    /// </summary>
    internal enum AddressMode
    {
        /// <summary>Both parameters are register identifier bytes.</summary>
        RegisterRegister = 0,
        /// <summary>Parameter 1 is an immediate value; parameter 2 is a register.</summary>
        ValueRegister    = 1,
        /// <summary>Parameter 1 is a register; parameter 2 is an immediate value.</summary>
        RegisterValue    = 2,
        /// <summary>Both parameters are immediate values.</summary>
        ValueValue       = 3,
    }
}
