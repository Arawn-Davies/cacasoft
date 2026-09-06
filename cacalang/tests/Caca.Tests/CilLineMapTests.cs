using System.Linq;
using System.Text;
using Caca.CilBackend;

namespace Caca.Tests;

/// <summary>
/// Exercises <see cref="CilEmitter.Emit(CompilationUnit, IReadOnlyDictionary{string, FunctionSymbol}, out string, out IReadOnlyList{(int, int)})"/>'s
/// source-line map: the (byte offset, cacalang source line) pairs a
/// step-by-step debugger uses to show the source line behind the CIL
/// instruction currently executing.
/// </summary>
public class CilLineMapTests
{
    /// <summary>
    /// Independently recomputes every byte offset the emitted text could
    /// possibly produce, using the same classification rule CilEmitter's own
    /// Line/LineDb split applies (label/comment/blank = 0 bytes, DB = its
    /// exact quoted UTF-8 byte length, anything else = one 6-byte
    /// instruction) — but reimplemented here, decoupled from the emitter's
    /// internal counter, so a mistake in one can't hide behind the other.
    /// </summary>
    private static HashSet<int> ValidByteOffsets(string program)
    {
        var offsets = new HashSet<int> { 0 };
        var offset = 0;

        foreach (var rawLine in program.Replace("\r", "").Split('\n'))
        {
            var trimmed = rawLine.TrimEnd();
            if (trimmed.Length == 0 || trimmed.EndsWith(':') || trimmed.StartsWith(';'))
            {
                // 0 bytes: blank line, label, or comment.
            }
            else if (trimmed.StartsWith("DB "))
            {
                var operand = trimmed[3..].Trim();
                Assert.True(operand.StartsWith('"') && operand.EndsWith('"'), $"unexpected DB operand: {trimmed}");
                var inner = operand[1..^1].Replace("\\\"", "\"").Replace("\\\\", "\\");
                offset += Encoding.UTF8.GetByteCount(inner);
            }
            else
            {
                offset += 6;
            }

            offsets.Add(offset);
        }

        return offsets;
    }

    [Fact]
    public void LineMap_ByteOffsetsLandOnRealInstructionBoundaries()
    {
        const string source = """
        var n = 0;
        n = n + 1;
        print n;
        """;

        var compilation = Compilation.Create(source);
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var diagnostics = CilEmitter.Emit(compilation.Program, compilation.Functions, out var text, out var lineMap);
        Assert.Empty(diagnostics);
        Assert.NotEmpty(lineMap);

        var validOffsets = ValidByteOffsets(text);
        foreach (var (byteOffset, sourceLine) in lineMap)
        {
            Assert.Contains(byteOffset, validOffsets);
            Assert.True(sourceLine >= 1, $"non-positive source line {sourceLine} for byte offset {byteOffset}");
        }

        for (var i = 1; i < lineMap.Count; i++)
        {
            Assert.True(
                lineMap[i].ByteOffset >= lineMap[i - 1].ByteOffset,
                $"lineMap must be non-decreasing in byte offset: entry {i - 1}={lineMap[i - 1]}, entry {i}={lineMap[i]}");
        }

        // Every one of the three statements' own lines (1, 2, 3) must be represented.
        var sourceLines = lineMap.Select(e => e.SourceLine).ToHashSet();
        Assert.Contains(1, sourceLines);
        Assert.Contains(2, sourceLines);
        Assert.Contains(3, sourceLines);
    }

    /// <summary>
    /// A program with a loop and an if/else must report the SAME source line
    /// on every iteration the loop body executes — the map is built once at
    /// compile time from the AST, not per dynamic execution, so it cannot
    /// distinguish "iteration 3" from "iteration 1"; it only has to agree,
    /// every time, on which line a given byte offset belongs to.
    /// </summary>
    [Fact]
    public void LineMap_CoversLoopAndConditionalBodies()
    {
        const string source = """
        for i = 1 to 3 do
            if i == 2 then
                print "two";
            else
                print i;
            end;
        end;
        """;

        var compilation = Compilation.Create(source);
        Assert.True(compilation.Succeeded, string.Join(Environment.NewLine, compilation.FormatDiagnostics()));

        var diagnostics = CilEmitter.Emit(compilation.Program, compilation.Functions, out var text, out var lineMap);
        Assert.Empty(diagnostics);

        var validOffsets = ValidByteOffsets(text);
        foreach (var (byteOffset, _) in lineMap)
        {
            Assert.Contains(byteOffset, validOffsets);
        }

        // Lines 2 (if), 3 (print "two"), and 5 (print i) must all be reachable in the map.
        var sourceLines = lineMap.Select(e => e.SourceLine).ToHashSet();
        Assert.Contains(2, sourceLines);
        Assert.Contains(3, sourceLines);
        Assert.Contains(5, sourceLines);
    }

    /// <summary>A program with diagnostics reports an empty map, not a partial or stale one.</summary>
    [Fact]
    public void LineMap_EmptyWhenCompilationHasDiagnostics()
    {
        var compilation = Compilation.Create("print 1.5;"); // float literal: rejected by this backend
        Assert.True(compilation.Succeeded); // succeeds at the compiler level; CilEmitter itself rejects float

        var diagnostics = CilEmitter.Emit(compilation.Program, compilation.Functions, out _, out var lineMap);
        Assert.NotEmpty(diagnostics);
        Assert.Empty(lineMap);
    }
}
