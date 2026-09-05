using System;
using System.Text;
using System.Linq;
using Caca.VM.Handlers;

namespace Caca.VM
{
    /// <summary>
    /// Process-wide settings shared by all VM components.
    /// The runtime (Caca.VM.Cli) sets <see cref="console"/> to a real implementation
    /// before executing any bytecode; the VM library ships with a <see cref="NullConsole"/>
    /// default so that unit tests or headless hosts need not wire up I/O manually.
    /// </summary>
    public static class Globals
    {
        /// <summary>
        /// Null console for the Virtual Machine default implementation.
        /// The runtime would contain the override Console implementation instead.  
        /// </summary>
        /// <returns>An empty console that provides no I/O</returns>
        public static VConsole console = new NullConsole();

        /// <summary>
        /// Default RAM allocation for a new VM instance, in bytes (1 MB).
        /// All VM instantiation sites should use this constant so that the
        /// RAM size can be changed in one place.
        /// </summary>
        public const int DefaultRamSize = 1_048_576;

        /// <summary>
        /// When <c>true</c>, the VM and standard-library interrupts emit additional
        /// diagnostic output (e.g. "KEI 0x01: ...") to <see cref="console"/>.
        /// Set to <c>false</c> for normal execution.
        /// </summary>
        public static bool DebugMode = false;
    }
}