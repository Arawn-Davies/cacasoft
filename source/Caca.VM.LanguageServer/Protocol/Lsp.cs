using System.Text.Json.Nodes;

namespace Caca.VM.LanguageServer.Protocol;

/// <summary>
/// The handful of Language Server Protocol shapes this server uses.
/// </summary>
/// <remarks>
/// Protocol positions are zero-based in both line and character; the compiler's
/// <see cref="global::Caca.VM.Compiler.BuildException.SrcLineNumber"/> is one-based.
/// The compiler doesn't track columns or end positions, so every diagnostic spans
/// the whole line — the finest granularity actually available.
/// </remarks>
public static class Lsp
{
    /// <summary>The protocol's severity for an error.</summary>
    private const int SeverityError = 1;

    public static JsonObject Position(int line, int character) => new()
    {
        ["line"] = Math.Max(0, line),
        ["character"] = Math.Max(0, character),
    };

    /// <summary>A range spanning an entire (one-based) source line.</summary>
    public static JsonObject WholeLineRange(int srcLineNumber) => new()
    {
        ["start"] = Position(srcLineNumber - 1, 0),
        // No column info is available, so the end just needs to be visibly past the
        // start — editors clip a too-long range to the actual line length.
        ["end"] = Position(srcLineNumber - 1, 1000),
    };

    public static JsonObject Diagnostic(int srcLineNumber, string message) => new()
    {
        ["range"] = WholeLineRange(srcLineNumber),
        ["severity"] = SeverityError,
        ["code"] = "CIL001",
        ["source"] = "cil",
        ["message"] = message,
    };
}
