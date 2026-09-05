using System;
using System.Collections.Generic;

namespace Caca.VM.Compiler
{
    /// <summary>
    /// Maps CacaVM register names to their identifier bytes (0xF0–0xFE).
    /// </summary>
    internal static class Registers
    {
        private static readonly Dictionary<string, byte> _map =
            new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase)
        {
            { "PC", 0xF0 },
            { "IP", 0xF1 },
            { "SP", 0xF2 },
            { "SS", 0xF3 },
            { "A",  0xF4 },
            { "AL", 0xF5 },
            { "AH", 0xF6 },
            { "B",  0xF7 },
            { "BL", 0xF8 },
            { "BH", 0xF9 },
            { "C",  0xFA },
            { "CL", 0xFB },
            { "CH", 0xFC },
            { "X",  0xFD },
            { "Y",  0xFE },
        };

        public static bool IsRegister(string token) =>
            _map.ContainsKey(token);

        public static byte GetByte(string token)
        {
            if (_map.TryGetValue(token, out byte b))
                return b;
            throw new ArgumentException($"Unknown register '{token}'.");
        }
    }
}
