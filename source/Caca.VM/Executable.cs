using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace Caca.VM
{
    public class Executable
    {
        /// <summary>
        /// Magic bytes for a valid .ilc file: "CIL\0"
        /// </summary>
        private static readonly byte[] IlcMagic = { 0x43, 0x49, 0x4C, 0x00 };

        /// <summary>
        /// Section type identifier for the code section.
        /// </summary>
        private const ushort SectionTypeCode = 0x0001;

        /// <summary>
        /// Loads and runs a CIL application.
        /// Accepts either a raw bytecode buffer or a structured .ilc binary.
        ///
        /// .ilc file layout (§8 of the specification):
        ///   Offset 0 : 4 bytes  Magic  (0x43 0x49 0x4C 0x00 = "CIL\0")
        ///   Offset 4 : 2 bytes  Format version (little-endian)
        ///   Offset 6 : 2 bytes  Section count  (little-endian)
        ///   Each section:
        ///     Offset 0 : 2 bytes  Section type   (little-endian)
        ///     Offset 2 : 4 bytes  Section length (little-endian)
        ///     Offset 6 : N bytes  Section data
        ///
        /// The first section of type 0x0001 (code) is loaded into the VM.
        /// Files without the magic header are treated as raw bytecode.
        /// </summary>
        public static void Run(byte[] application)
        {
            byte[] code = ExtractCode(application);
            // Allocate 1 MB of RAM (see Globals.DefaultRamSize)
            VM virtualMachine = new VM(code, Globals.DefaultRamSize);
            virtualMachine.Execute();
        }

        /// <summary>
        /// Extracts the executable code from an application buffer.
        /// Returns the code section payload for .ilc files, or the raw buffer otherwise.
        /// </summary>
        private static byte[] ExtractCode(byte[] application)
        {
            if (!HasIlcMagic(application))
                return application;

            ushort sectionCount = (ushort)(application[6] | (application[7] << 8));
            int offset = 8;

            for (int i = 0; i < sectionCount; i++)
            {
                if (offset + 6 > application.Length)
                    throw new Exception("[CRITICAL ERROR] Malformed .ilc file: unexpected end of section table.");

                ushort sectionType = (ushort)(application[offset] | (application[offset + 1] << 8));
                int sectionLength = application[offset + 2]
                                  | (application[offset + 3] << 8)
                                  | (application[offset + 4] << 16)
                                  | (application[offset + 5] << 24);
                offset += 6;

                if (offset + sectionLength > application.Length)
                    throw new Exception($"[CRITICAL ERROR] Malformed .ilc file: section {i} length {sectionLength} exceeds file size.");

                if (sectionType == SectionTypeCode)
                {
                    byte[] codeSection = new byte[sectionLength];
                    Array.Copy(application, offset, codeSection, 0, sectionLength);
                    return codeSection;
                }

                offset += sectionLength;
            }

            throw new Exception("[CRITICAL ERROR] .ilc file contains no code section (type 0x0001).");
        }

        /// <summary>
        /// Returns true if the buffer starts with the CIL magic bytes ("CIL\0").
        /// </summary>
        private static bool HasIlcMagic(byte[] data)
        {
            if (data.Length < 8)
                return false;
            for (int i = 0; i < IlcMagic.Length; i++)
                if (data[i] != IlcMagic[i])
                    return false;
            return true;
        }
    }
}