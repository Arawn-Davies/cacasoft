using System;
using System.Collections.Generic;
using Xunit;
using Caca.VM;

namespace Caca.VM.Tests
{
    /// <summary>
    /// Tests for <see cref="Caca.VM.Executable"/>'s .ilc container handling (§8 of the
    /// specification). The container bytes here are built independently from the spec's
    /// documented layout, not copied from MainForm.cs's private writer — this checks
    /// Executable.Run actually matches the spec, not just that two private
    /// implementations happen to agree with each other.
    /// </summary>
    [Collection("VM")]
    public sealed class ExecutableTests
    {
        private readonly TestConsole _console = new TestConsole();

        /// <summary>
        /// Builds a spec-compliant .ilc container: 4-byte magic, 2-byte version (LE),
        /// 2-byte section count (LE), then one section (2-byte type + 4-byte length,
        /// both LE, followed by the section data).
        /// </summary>
        private static byte[] BuildIlcContainer(byte[] magic, byte[] code)
        {
            var bytes = new List<byte>();
            bytes.AddRange(magic);                                  // magic (4 bytes)
            bytes.AddRange(new byte[] { 0x02, 0x00 });               // version 2 (LE)
            bytes.AddRange(new byte[] { 0x01, 0x00 });               // section count = 1 (LE)
            bytes.AddRange(new byte[] { 0x01, 0x00 });               // section type = 0x0001 (code)
            bytes.AddRange(BitConverter.GetBytes(code.Length));      // section length (LE, 4 bytes)
            bytes.AddRange(code);                                    // section data
            return bytes.ToArray();
        }

        private string Run(byte[] application)
        {
            Caca.VM.Globals.console = _console;
            Caca.VM.Globals.DebugMode = false;
            _console.Reset();
            Executable.Run(application);
            return _console.Output;
        }

        // A minimal program: print 'H' then halt. Enough to prove the VM actually ran
        // the extracted code section, not garbage.
        private static readonly byte[] PrintHAndHalt =
        {
            0x06, 0xF5, 0x01, 0x00, 0x00, 0x00, // MOV AL, 0x01  (write-char mode)
            0x06, 0xF6, 0x48, 0x00, 0x00, 0x00, // MOV AH, 'H'
            0xAC, 0x01, 0x00, 0x00, 0x00, 0x00, // KEI 0x01
            0xAC, 0x02, 0x00, 0x00, 0x00, 0x00, // KEI 0x02 (halt)
        };

        /// <summary>
        /// A container using the real CIL magic bytes (0x43 0x49 0x4C 0x00, "CIL\0")
        /// must have its header stripped and its code section executed.
        /// </summary>
        [Fact]
        public void Run_WithValidIlcContainer_ExtractsAndExecutesCodeSection()
        {
            byte[] cilMagic = { 0x43, 0x49, 0x4C, 0x00 };
            byte[] container = BuildIlcContainer(cilMagic, PrintHAndHalt);

            string output = Run(container);

            Assert.Equal("H" + "Halting!\n", output);
        }

        /// <summary>
        /// Regression test for the exact bug this fix corrected: a buffer starting with
        /// the OLD "AIL\0" bytes (0x41 0x49 0x4C 0x00) must NOT be recognised as a valid
        /// container — CIL is the only magic this VM accepts now. Falling through to the
        /// raw-bytecode path means these bytes get executed directly as instructions
        /// rather than treated as a header, so this must not produce "H".
        /// </summary>
        [Fact]
        public void Run_WithOldAilMagicBytes_IsNotTreatedAsContainer()
        {
            byte[] oldAilMagic = { 0x41, 0x49, 0x4C, 0x00 };
            byte[] notAContainer = BuildIlcContainer(oldAilMagic, PrintHAndHalt);

            string output = Run(notAContainer);

            Assert.NotEqual("H" + "Halting!\n", output);
        }

        /// <summary>Raw bytecode with no header at all must still run directly, unchanged.</summary>
        [Fact]
        public void Run_WithRawBytecodeNoHeader_ExecutesDirectly()
        {
            string output = Run(PrintHAndHalt);

            Assert.Equal("H" + "Halting!\n", output);
        }

        /// <summary>
        /// A container whose declared section table runs past the end of the buffer
        /// must raise a clean diagnostic rather than an out-of-bounds crash.
        /// </summary>
        [Fact]
        public void Run_WithTruncatedSectionTable_ThrowsCleanDiagnostic()
        {
            byte[] cilMagic = { 0x43, 0x49, 0x4C, 0x00 };
            var bytes = new List<byte>();
            bytes.AddRange(cilMagic);
            bytes.AddRange(new byte[] { 0x02, 0x00 }); // version
            bytes.AddRange(new byte[] { 0x01, 0x00 }); // section count = 1
            // No section entry follows — the table is truncated.

            var ex = Assert.Throws<Exception>(() => Run(bytes.ToArray()));
            Assert.Contains("Malformed .ilc file", ex.Message);
        }
    }
}
