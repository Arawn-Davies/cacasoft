namespace Caca.VM.Compiler
{
    /// <summary>
    /// Addressing modes for CacaVM instructions.
    /// Values match the 2-bit field in byte 0 of each instruction (bits 1-0).
    /// </summary>
    public enum AddressMode
    {
        RegisterRegister = 0,
        ValueRegister    = 1,
        RegisterValue    = 2,
        ValueValue       = 3,
    }
}
