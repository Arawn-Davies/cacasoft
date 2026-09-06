using System.Diagnostics;
using Caca.CilBackend;
using Caca.Diagnostics;

namespace Caca.Tests;

/// <summary>
/// A fact that only runs where a CacaVM CLI build can be found. CacaVM
/// (github.com/Arawn-Davies/CacaVM) is a separate project this repo does not
/// vendor or build — see <see cref="CilEmitterTests.FindCacaVmCli"/> for
/// where it is expected. Skipping is more honest than failing when it is
/// not present, the same reasoning <c>CcFactAttribute</c> uses for the C
/// backend's <c>cc</c> dependency.
/// </summary>
public sealed class CacaVmFactAttribute : FactAttribute
{
    public CacaVmFactAttribute()
    {
        if (CilEmitterTests.CacaVmCliPath is null)
        {
            Skip = "no CacaVM CLI build was found (set CACAVM_CLI to its Caca.VM.Cli.dll, " +
                   "or build ../CacaVM or ../Artemis-IL alongside this repo)";
        }
    }
}

/// <summary>The same, for theories.</summary>
public sealed class CacaVmTheoryAttribute : TheoryAttribute
{
    public CacaVmTheoryAttribute()
    {
        if (CilEmitterTests.CacaVmCliPath is null)
        {
            Skip = "no CacaVM CLI build was found (set CACAVM_CLI to its Caca.VM.Cli.dll, " +
                   "or build ../CacaVM or ../Artemis-IL alongside this repo)";
        }
    }
}

/// <summary>
/// Exercises the CacaVM backend by assembling and running its output on the
/// actual virtual machine, which is what proves the emitted CIL is not just
/// well-formed text but does the right thing — the same standard
/// <c>CEmitterTests</c> holds the C backend to.
/// </summary>
/// <remarks>
/// The v1 subset this backend covers deliberately excludes <c>float</c>,
/// <c>extern func</c>, <c>read_int</c>/<c>read_string</c>, and every string
/// operation except printing a literal directly — see
/// <see cref="CilEmitter"/>'s own remarks. The theory list below is the C
/// backend's parity suite with those cases removed, not a different set of
/// programs; every case that survives must still agree with the interpreter.
/// </remarks>
public class CilEmitterTests : IDisposable
{
    internal static readonly string? CacaVmCliPath = FindCacaVmCli();

    private readonly string _directory =
        Directory.CreateTempSubdirectory("caca-cacavm-").FullName;

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best effort; failing to remove a temporary directory
            // must not fail the test that produced it.
        }
    }

    /// <summary>
    /// Locates a built <c>Caca.VM.Cli.dll</c>. CacaVM is a separate repo
    /// (github.com/Arawn-Davies/CacaVM) this one does not vendor, so there is
    /// no fixed relative path that works for every checkout: an explicit
    /// <c>CACAVM_CLI</c> environment variable wins if set, otherwise this
    /// looks for a sibling checkout named either <c>CacaVM</c> (the current
    /// name) or <c>Artemis-IL</c> (the name it was renamed from — plenty of
    /// existing local clones still use it), built Release or Debug.
    /// </summary>
    private static string? FindCacaVmCli()
    {
        var overridePath = Environment.GetEnvironmentVariable("CACAVM_CLI");

        if (!string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
        {
            return overridePath;
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Cacalang.sln")))
        {
            directory = directory.Parent;
        }

        var repoParent = directory?.Parent;

        if (repoParent is null)
        {
            return null;
        }

        foreach (var siblingName in new[] { "CacaVM", "Artemis-IL" })
        {
            foreach (var configuration in new[] { "Release", "Debug" })
            {
                var candidate = Path.Combine(
                    repoParent.FullName, siblingName, "source", "Caca.VM.Cli", "bin", configuration,
                    "net10.0", "Caca.VM.Cli.dll");

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Compiles a program to CIL, runs it on the actual CacaVM, and returns
    /// everything it printed — including the VM's own "Halting!\n" line
    /// from the halt interrupt, which every program ends with.
    /// </summary>
    private string RunCacaVm(string source, string input = "", [System.Runtime.CompilerServices.CallerMemberName] string name = "")
    {
        var compilation = Compilation.Create(source);
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var diagnostics = CilEmitter.Emit(compilation.Program, compilation.Functions, out var text);
        Assert.Empty(diagnostics);

        var cilPath = Path.Combine(_directory, $"{name}.cil");
        File.WriteAllText(cilPath, text);

        using var process = Process.Start(new ProcessStartInfo("dotnet", $"\"{CacaVmCliPath}\" \"{cilPath}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        process.StandardInput.Write(input);
        process.StandardInput.Close();

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        var exited = process.WaitForExit(10_000);
        Assert.True(exited, $"CacaVM did not exit within 10s.\nstdout:\n{output}\nstderr:\n{error}");

        return output;
    }

    [CacaVmFact]
    public void CacaVm_backend_compiles_and_runs_hello_world()
    {
        Assert.Equal("hello, world\nHalting!\n", RunCacaVm("""print "hello, world";"""));
    }

    /// <summary>
    /// The CacaVM backend must agree with the interpreter on every program in
    /// its v1 subset — the same discipline <c>C_backend_matches_the_interpreter</c>
    /// holds the C backend to, minus float, extern, read_int/read_string, and
    /// any string operation beyond printing a literal.
    /// </summary>
    [CacaVmTheory]
    [InlineData("var x = 2; var y = 4; print y / x;")]
    [InlineData("for i = 1 to 4 do print i * i; end;")]
    [InlineData("print (1 + 2) * (3 - 4) / 1;")]
    [InlineData("var i = 1; while i <= 5 do if i == 3 then i = i + 1; continue; end; print i * i; i = i + 1; end;")]
    [InlineData("for i = 1 to 20 do if i > 4 then break; end; print i >= 2 && i <= 3; end;")]
    [InlineData("for i = 1 to 10 do if i % 2 == 0 then continue; end; print i; end;")]
    [InlineData("func fib(n: int): int do if n < 2 then return n; end; return fib(n - 1) + fib(n - 2); end for i = 0 to 10 do print fib(i); end;")]
    [InlineData("print 2147483647 + 1; print -2147483647 - 2; print 65536 * 65536;")]
    // Regression: an early return from inside an if inside a for loop, in a
    // function with its own locals. Caught a real bug — the epilogue's
    // stack-depth bookkeeping leaked from the returning branch into the
    // code emitted right after the if (the loop's own tail check), which
    // only ever runs on the OTHER branch at runtime and needs to see the
    // depth as though the return had never been emitted at all.
    [InlineData("func firstEven(limit: int): int do for i = 1 to limit do if i % 2 == 0 then return i; end; end; return -1; end print firstEven(10); print firstEven(1); var x = 3; print firstEven(10) + x;")]
    public void CacaVm_backend_matches_the_interpreter(string source)
    {
        Assert.Equal(TestHost.Run(source) + "Halting!\n", RunCacaVm(source));
    }

    [CacaVmTheory]
    [InlineData("print 1 < 2; print 2 == 2; print 1 <= 1; print 3 >= 4; print !(1 == 1); print (1 == 1) || (2 == 3);")]
    [InlineData("print 7 % 3; print -7 % 3; print 7 % -3; print -7 % -3;")]
    [InlineData("func add3(a: int, b: int, c: int): int do return a + b + c; end print add3(1, 2, 3); print add3(10, -5, 2);")]
    [InlineData("for i = 1 to 3 do for j = 1 to 3 do print i * 10 + j; end; end;")]
    [InlineData("func isEven(n: int): bool do return n % 2 == 0; end func describe(n: int): int do if isEven(n) then return 1; end; return 0; end print describe(4); print describe(5);")]
    [InlineData("func findFirst(limit: int): int do var k = 1; while k <= limit do if k * k > 20 then return k; end; k = k + 1; end; return -1; end print findFirst(10); print findFirst(2);")]
    public void CacaVm_backend_matches_the_interpreter_more_cases(string source)
    {
        Assert.Equal(TestHost.Run(source) + "Halting!\n", RunCacaVm(source));
    }

    [CacaVmFact]
    public void CacaVm_backend_division_by_zero_is_a_runtime_error()
    {
        var compilation = Compilation.Create("var z = 0; print 1 / z;");
        Assert.True(compilation.Succeeded);

        var diagnostics = CilEmitter.Emit(compilation.Program, compilation.Functions, out var text);
        Assert.Empty(diagnostics);

        var cilPath = Path.Combine(_directory, "divzero.cil");
        File.WriteAllText(cilPath, text);

        using var process = Process.Start(new ProcessStartInfo("dotnet", $"\"{CacaVmCliPath}\" \"{cilPath}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;

        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000);

        Assert.Contains("division by zero", output);
    }

    [Fact]
    public void CacaVm_target_rejects_extern_functions()
    {
        // No process is started here, so no CacaVM build is needed.
        var compilation = Compilation.Create(
            """extern func sqrt(x: float): float from "System.Math.Sqrt"; print sqrt(4.0);""");
        Assert.True(compilation.Succeeded);

        var diagnostics = CilEmitter.Emit(compilation.Program, compilation.Functions, out var text);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ExternNotAvailableInCacaVm);
        Assert.Empty(text);
    }

    [Fact]
    public void CacaVm_target_rejects_float()
    {
        var compilation = Compilation.Create("print 1.5;");
        Assert.True(compilation.Succeeded);

        var diagnostics = CilEmitter.Emit(compilation.Program, compilation.Functions, out var text);
        var error = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticCode.FloatNotAvailableInCacaVm, error.Code);
        Assert.Empty(text);
    }

    [Fact]
    public void CacaVm_target_rejects_non_literal_strings()
    {
        var compilation = Compilation.Create("""var s = "hi"; print s;""");
        Assert.True(compilation.Succeeded);

        var diagnostics = CilEmitter.Emit(compilation.Program, compilation.Functions, out var text);
        Assert.NotEmpty(diagnostics);
        Assert.All(diagnostics, d => Assert.Equal(DiagnosticCode.StringNotLiteralInCacaVm, d.Code));
        Assert.Empty(text);
    }

    [Fact]
    public void CacaVm_target_rejects_read_string()
    {
        var compilation = Compilation.Create("""var s = ""; read_string s; print s;""");
        Assert.True(compilation.Succeeded);

        var diagnostics = CilEmitter.Emit(compilation.Program, compilation.Functions, out var text);
        Assert.Contains(diagnostics, d => d.Code == DiagnosticCode.ReadNotAvailableInCacaVm);
        Assert.Empty(text);
    }

    [CacaVmFact]
    public void CacaVm_backend_prints_string_literal_and_bool()
    {
        Assert.Equal(
            "hi\ntrue\nfalse\n-2147483648\nHalting!\n",
            RunCacaVm("""print "hi"; print true; print false; print -2147483647 - 1;"""));
    }

    // ── read_int — now that CacaVM's standard library has an atoi ────────────

    [CacaVmTheory]
    [InlineData("var n = 0; read_int n; print n; print n + 1;", "7\n")]
    [InlineData("var n = 0; read_int n; print n;", "-42\n")]
    public void CacaVm_backend_read_int_matches_the_interpreter(string source, string input)
    {
        Assert.Equal(TestHost.Run(source, input) + "Halting!\n", RunCacaVm(source, input));
    }

    /// <summary>read_int inside a function, called more than once — a fresh read each time, sharing one buffer.</summary>
    [CacaVmFact]
    public void CacaVm_backend_read_int_inside_a_function_called_twice()
    {
        const string source = """
            func doubleIt(): int do
                var n = 0;
                read_int n;
                return n * 2;
            end
            print doubleIt();
            print doubleIt();
            """;
        const string input = "5\n10\n";
        Assert.Equal(TestHost.Run(source, input) + "Halting!\n", RunCacaVm(source, input));
    }

    /// <summary>
    /// read_int inside a for loop's body (its own local, alongside the loop's
    /// own locals), and inside a for loop nested in a function with an early
    /// return — the exact shape that caught the _depth-leak regression this
    /// suite already guards against, now with a read buffer sharing the same
    /// reserved-slot bookkeeping.
    /// </summary>
    [CacaVmFact]
    public void CacaVm_backend_read_int_inside_loops_matches_the_interpreter()
    {
        const string source = """
            var total = 0;
            for i = 1 to 3 do
                var n = 0;
                read_int n;
                total = total + n;
            end;
            print total;

            func firstPositive(limit: int): int do
                for i = 1 to limit do
                    var n = 0;
                    read_int n;
                    if n > 0 then return n; end;
                end;
                return -1;
            end
            print firstPositive(5);
            """;
        const string input = "1\n2\n3\n-1\n-2\n9\n4\n4\n";
        Assert.Equal(TestHost.Run(source, input) + "Halting!\n", RunCacaVm(source, input));
    }

    /// <summary>
    /// The full showcase sample — arithmetic, comparisons, bool logic,
    /// if/else-if chains, while/for with break and continue, recursion,
    /// mutual recursion, an early return from a loop nested in a function,
    /// and read_int — through the actual VM, matching the interpreter.
    /// </summary>
    [CacaVmFact]
    public void CacaVm_backend_matches_the_interpreter_on_the_showcase_sample()
    {
        var source = File.ReadAllText(TestHost.SamplePath("cacavm_showcase.caca"));
        const string input = "3\n4\n";
        Assert.Equal(TestHost.Run(source, input) + "Halting!\n", RunCacaVm(source, input));
    }

    /// <summary>
    /// Three of the general-purpose samples — written for the language as a
    /// whole, not for this target specifically — happen to already stay
    /// inside CacaVM's v1 subset. Confirmed by actually running them on the
    /// VM, not assumed from the fact that they compile.
    /// </summary>
    [CacaVmFact]
    public void CacaVm_backend_matches_the_interpreter_on_the_helloworld_sample()
    {
        var source = File.ReadAllText(TestHost.SamplePath("helloworld.caca"));
        const string input = "7\n";
        Assert.Equal(TestHost.Run(source, input) + "Halting!\n", RunCacaVm(source, input));
    }

    [CacaVmFact]
    public void CacaVm_backend_matches_the_interpreter_on_the_loop_sample()
    {
        var source = File.ReadAllText(TestHost.SamplePath("loop.caca"));
        const string input = "2\n";
        Assert.Equal(TestHost.Run(source, input) + "Halting!\n", RunCacaVm(source, input));
    }

    [CacaVmFact]
    public void CacaVm_backend_matches_the_interpreter_on_the_fizzbuzz_sample()
    {
        var source = File.ReadAllText(TestHost.SamplePath("fizzbuzz.caca"));
        const string input = "20\n";
        Assert.Equal(TestHost.Run(source, input) + "Halting!\n", RunCacaVm(source, input));
    }
}
