using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Caca.VM;

namespace Caca.VM.Cli
{
    /// <summary>
    /// <see cref="Caca.VM.Handlers.VConsole"/> implementation that routes all VM I/O
    /// to the host process's standard console (<see cref="System.Console"/>).
    /// This is the default console used by the Caca.VM.Cli executable.
    /// </summary>
    public class CliConsole : Caca.VM.Handlers.VConsole
	{
		/// <inheritdoc/>
		/// <remarks>
		/// Writes <c>text</c> followed by a literal <c>\n</c> rather than
		/// <see cref="Console.WriteLine(string)"/>, which appends
		/// <see cref="Environment.NewLine"/> — "\r\n" on Windows. A CIL
		/// program's console output must be the same on every host, the same
		/// way the test suite's in-process console already hardcodes '\n';
		/// this keeps the real CLI consistent with it.
		/// </remarks>
		public override void WriteLine(string text)
		{
			Console.Write(text);
			Console.Write('\n');
		}

		/// <inheritdoc/>
		public override void Write(char ch)
		{
			Console.Write(ch);
		}

		/// <inheritdoc/>
		public override void Write(string text)
		{
			Console.Write(text);
		}

		/// <inheritdoc/>
		/// <remarks>
		/// Reads one keypress (echoed to the terminal), converts the Unicode key character
		/// to its ASCII byte value, and returns it.
		/// </remarks>
		public override byte Read()
		{
			char t = Console.ReadKey(false).KeyChar;
            byte[] b = { (byte)t };
			ASCIIEncoding.Convert(new UnicodeEncoding(), new ASCIIEncoding(), b);
			return b[0];
		}

		/// <inheritdoc/>
		public override string ReadLine()
		{
			return Console.ReadLine();
		}
	}
}
