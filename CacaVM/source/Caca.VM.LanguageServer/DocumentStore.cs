namespace Caca.VM.LanguageServer;

/// <summary>
/// The files the editor has open, and the last compile attempt for each.
/// </summary>
/// <remarks>
/// Unlike a full binder, <see cref="global::Caca.VM.Compiler.Compiler"/> stops at the
/// first problem it finds (it throws rather than collecting a list of diagnostics) —
/// so a document has at most one diagnostic at a time here, not the "every error at
/// once" an editor would ideally want. Good enough for live red-squiggle feedback;
/// fixing one error re-triggers a recompile that surfaces the next.
/// </remarks>
public sealed class DocumentStore
{
    private readonly Dictionary<string, Document> _documents = new(StringComparer.Ordinal);

    public Document Update(string uri, string text)
    {
        var document = new Document(uri, text, TryCompile(text));
        _documents[uri] = document;
        return document;
    }

    public void Remove(string uri) => _documents.Remove(uri);

    public Document? Find(string uri) => _documents.GetValueOrDefault(uri);

    /// <summary>
    /// Compiles <paramref name="text"/> and returns the resulting error, or null on
    /// success. Catches any exception, not just <see cref="global::Caca.VM.Compiler.BuildException"/>
    /// — a language server must keep serving the editor session no matter what a
    /// malformed or pathological document throws, so an unexpected exception becomes
    /// a line-1 diagnostic instead of taking the whole process down.
    /// </summary>
    private static CompileError? TryCompile(string text)
    {
        try
        {
            new global::Caca.VM.Compiler.Compiler(text).Compile();
            return null;
        }
        catch (global::Caca.VM.Compiler.BuildException ex)
        {
            return new CompileError(ex.SrcLineNumber, ex.Message);
        }
        catch (Exception ex)
        {
            return new CompileError(1, $"internal compiler error: {ex.Message}");
        }
    }
}

/// <summary>One open file and the error from its last compile attempt, if any.</summary>
public sealed record Document(string Uri, string Text, CompileError? Error);

/// <summary>A compile failure reduced to what the LSP diagnostic actually needs.</summary>
public sealed record CompileError(int SrcLineNumber, string Message);
