using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace Caca.VM.Handlers
{
    /// <summary>
    /// Abstract base class for the VM's I/O console.
    /// Implement this class to redirect VM output to any target (system console,
    /// GUI text box, test buffer, etc.) and supply the implementation via
    /// <see cref="Caca.VM.Globals.console"/> before executing bytecode.
    /// </summary>
    public abstract class VConsole
    {
        /// <summary>Writes <paramref name="text"/> followed by a newline to the output.</summary>
        public abstract void WriteLine(string text);

        /// <summary>Writes a single character <paramref name="ch"/> to the output without a newline.</summary>
        public abstract void Write(char ch);

        /// <summary>Writes <paramref name="text"/> to the output without a newline.</summary>
        public abstract void Write(string text);

        /// <summary>Reads a single byte from the input and returns it.</summary>
        public abstract byte Read();

        /// <summary>Reads a full line of text from the input and returns it (without the line terminator).</summary>
        public abstract string ReadLine();
    }
}