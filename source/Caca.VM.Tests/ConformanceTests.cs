using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using Caca.VM.Handlers;

namespace Caca.VM.Tests
{
    /// <summary>
    /// Runs the C# reference VM against the shared, engine-agnostic corpus in
    /// /conformance/cases — see conformance/README.md. The Swift port
    /// (mac/CacaStudioMac/Tests/CacaVMKitTests) runs the exact same corpus via its
    /// own adapter; a case only counts once both agree on it.
    /// </summary>
    [Collection("VM")]
    public sealed class ConformanceTests
    {
        /// <summary>Console that replays a fixed list of stdin lines and captures all output.</summary>
        private sealed class ScriptedConsole : VConsole
        {
            private readonly System.Text.StringBuilder _output = new System.Text.StringBuilder();
            private readonly Queue<string> _lines;

            public ScriptedConsole(IEnumerable<string> lines) => _lines = new Queue<string>(lines);

            public string Output => _output.ToString();

            public override void Write(char ch) => _output.Append(ch);
            public override void Write(string text) => _output.Append(text);
            public override void WriteLine(string text)
            {
                _output.Append(text);
                _output.Append('\n');
            }

            public override byte Read() => _lines.Count > 0 && _lines.Peek().Length > 0
                ? (byte)_lines.Peek()[0]
                : (byte)0;

            public override string ReadLine() => _lines.Count > 0 ? _lines.Dequeue() : string.Empty;
        }

        /// <summary>
        /// Walks up from this test assembly's own directory to find the repo root
        /// (the one containing both "source" and "conformance"), rather than relying
        /// on the test runner's current working directory.
        /// </summary>
        private static string ConformanceCasesDirectory()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "conformance", "cases")))
            {
                dir = dir.Parent;
            }

            if (dir == null)
            {
                throw new DirectoryNotFoundException(
                    "Could not locate conformance/cases by walking up from " + AppContext.BaseDirectory);
            }

            return Path.Combine(dir.FullName, "conformance", "cases");
        }

        public static IEnumerable<object[]> Cases()
        {
            string casesDir = ConformanceCasesDirectory();
            foreach (string dir in Directory.GetDirectories(casesDir).OrderBy(d => d))
            {
                yield return new object[] { Path.GetFileName(dir) };
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ConformanceCase_MatchesExpectedBehavior(string caseName)
        {
            string dir = Path.Combine(ConformanceCasesDirectory(), caseName);

            string source = File.ReadAllText(Path.Combine(dir, "source.cil"));

            string stdinPath = Path.Combine(dir, "stdin.txt");
            var input = File.Exists(stdinPath)
                ? File.ReadAllText(stdinPath).Split('\n')
                : Array.Empty<string>();

            string ramSizePath = Path.Combine(dir, "ramsize.txt");
            int ramSize = File.Exists(ramSizePath)
                ? int.Parse(File.ReadAllText(ramSizePath).Trim())
                : Globals.DefaultRamSize;

            string stdoutPath = Path.Combine(dir, "stdout.txt");
            string errorPath = Path.Combine(dir, "error.txt");
            bool hasStdout = File.Exists(stdoutPath);
            bool hasError = File.Exists(errorPath);

            Assert.True(hasStdout != hasError,
                $"{caseName}: exactly one of stdout.txt/error.txt must be present");

            var console = new ScriptedConsole(input);
            Caca.VM.Globals.console = console;
            Caca.VM.Globals.DebugMode = false;

            byte[] code = new global::Caca.VM.Compiler.Compiler(source).Compile();
            var vm = new VM(code, ramSize);

            if (hasStdout)
            {
                vm.Execute();
                string expected = File.ReadAllText(stdoutPath);
                Assert.Equal(expected, console.Output);
            }
            else
            {
                string expectedSubstring = File.ReadAllText(errorPath).Trim();
                var ex = Assert.Throws<Exception>(() => vm.Execute());
                Assert.Contains(expectedSubstring, ex.Message);
            }
        }
    }
}
