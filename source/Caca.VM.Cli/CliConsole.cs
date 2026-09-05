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
		public override void WriteLine(string text)
		{
			Console.WriteLine(text);
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
