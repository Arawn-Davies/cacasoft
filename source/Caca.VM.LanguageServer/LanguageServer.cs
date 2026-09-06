using System.Text.Json.Nodes;
using Caca.VM.LanguageServer.Protocol;

namespace Caca.VM.LanguageServer;

/// <summary>
/// A Language Server Protocol server for CIL: live compile errors on open/change.
/// No hover, go-to-definition, or references — <see cref="global::Caca.VM.Compiler.Compiler"/>
/// is a straight assembler with no symbol table to query, so those features have
/// nothing to be built on. Syntax highlighting is handled separately by the editor
/// extension's own TextMate grammar; this server only publishes diagnostics.
/// </summary>
public sealed class LanguageServer(JsonRpcConnection connection)
{
    private const int MethodNotFound = -32601;

    private readonly JsonRpcConnection _connection = connection;
    private readonly DocumentStore _documents = new();
    private bool _shuttingDown;

    /// <summary>Reads and answers messages until the client closes the connection.</summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var message = await _connection.ReadAsync(cancellationToken);

            if (message is null)
            {
                return;
            }

            await HandleAsync(message);

            if (_shuttingDown && (string?)message["method"] == "exit")
            {
                return;
            }
        }
    }

    private async Task HandleAsync(JsonObject message)
    {
        var method = (string?)message["method"];
        var id = message["id"];
        var parameters = message["params"];

        // A notification has no id and expects no reply.
        switch (method)
        {
            case "initialize":
                await _connection.SendResponseAsync(id, InitializeResult());
                return;

            case "initialized":
                return;

            case "textDocument/didOpen":
                await OpenAsync(parameters);
                return;

            case "textDocument/didChange":
                await ChangeAsync(parameters);
                return;

            case "textDocument/didClose":
                Close(parameters);
                return;

            case "shutdown":
                _shuttingDown = true;
                await _connection.SendResponseAsync(id, null);
                return;

            case "exit":
                _shuttingDown = true;
                return;

            default:
                if (id is not null)
                {
                    await _connection.SendErrorAsync(id, MethodNotFound, $"unsupported request '{method}'");
                }

                return;
        }
    }

    private static JsonObject InitializeResult() => new()
    {
        ["capabilities"] = new JsonObject
        {
            // The whole file is sent on every change. CIL programs are small, and
            // compiling one is fast enough that tracking incremental edits would
            // buy nothing.
            ["textDocumentSync"] = 1,
        },
        ["serverInfo"] = new JsonObject
        {
            ["name"] = "cil-langserver",
            ["version"] = typeof(LanguageServer).Assembly.GetName().Version?.ToString(3) ?? "unknown",
        },
    };

    private async Task OpenAsync(JsonNode? parameters)
    {
        var document = parameters?["textDocument"];
        var uri = (string?)document?["uri"];

        if (uri is null)
        {
            return;
        }

        await PublishDiagnosticsAsync(_documents.Update(uri, (string?)document?["text"] ?? string.Empty));
    }

    private async Task ChangeAsync(JsonNode? parameters)
    {
        var uri = (string?)parameters?["textDocument"]?["uri"];
        var changes = parameters?["contentChanges"] as JsonArray;

        if (uri is null || changes is null || changes.Count == 0)
        {
            return;
        }

        // With full synchronization the last change carries the whole document.
        var text = (string?)changes[^1]?["text"] ?? string.Empty;
        await PublishDiagnosticsAsync(_documents.Update(uri, text));
    }

    private void Close(JsonNode? parameters)
    {
        if ((string?)parameters?["textDocument"]?["uri"] is { } uri)
        {
            _documents.Remove(uri);
        }
    }

    private Task PublishDiagnosticsAsync(Document document)
    {
        var diagnostics = new JsonArray();

        if (document.Error is { } error)
        {
            diagnostics.Add(Lsp.Diagnostic(error.SrcLineNumber, error.Message));
        }

        return _connection.SendNotificationAsync("textDocument/publishDiagnostics", new JsonObject
        {
            ["uri"] = document.Uri,
            ["diagnostics"] = diagnostics,
        });
    }
}
