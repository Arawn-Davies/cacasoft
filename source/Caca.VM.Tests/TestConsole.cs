using System.Text;
using Caca.VM.Handlers;

namespace Caca.VM.Tests
{
    /// <summary>
    /// A <see cref="VConsole"/> implementation that captures all written output in a
    /// <see cref="StringBuilder"/> so tests can assert on the program's console output.
    /// </summary>
    internal sealed class TestConsole : VConsole
    {
        private readonly StringBuilder _output = new StringBuilder();

        /// <summary>Returns everything written to the console so far.</summary>
        public string Output => _output.ToString();

        /// <summary>Resets the captured output.</summary>
        public void Reset() => _output.Clear();

        public override void Write(char ch) => _output.Append(ch);

        public override void Write(string text) => _output.Append(text);

        public override void WriteLine(string text)
        {
            _output.Append(text);
            _output.Append('\n');
        }

        // Input stubs – tests that require input should override these.
        public override byte Read() => 0;
        public override string ReadLine() => string.Empty;
    }
}
