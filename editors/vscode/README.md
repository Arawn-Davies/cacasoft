# CIL for Visual Studio Code

Syntax highlighting and live errors for [CIL](../../README.md), the CacaVM
intermediate language.

## Installing

The extension is a thin client: the work is done by `cil-langserver`, which
is part of this repository.

1. Build and publish the server into the repository, where the committed
   workspace settings expect it:

   ```sh
   dotnet publish source/Caca.VM.LanguageServer -c Release -o artifacts/langserver
   ```

2. Install the extension:

   ```sh
   cd editors/vscode
   npm install
   npx @vscode/vsce package
   code --install-extension cil-0.1.0.vsix
   ```

   Or, to try it without packaging, open this folder in VS Code and press F5 to
   launch an Extension Development Host.

`.vscode/settings.json` points `cil.server.path` at
`${workspaceFolder}/artifacts/langserver/cil-langserver.dll`, so nothing needs
to be on your `PATH` beyond `dotnet` itself. Setting it to an empty string
falls back to `cil-langserver` from `PATH`.

### Why the server is run through `dotnet`

`dotnet publish` produces both a `.dll` and a native launcher beside it. The
extension runs the `.dll` through `dotnet`. The launcher finds the runtime
through the standard install locations and `DOTNET_ROOT`, neither of which
covers a .NET installed under the user's home directory — so it fails to
start in exactly the setup where an editor is most likely to launch it, with
an error the user never sees. `dotnet` is on `PATH`, so running the `.dll`
works wherever the compiler itself does.

## What the server provides

| Feature | Request |
|---|---|
| Errors as you type | `textDocument/publishDiagnostics` |

That's the whole feature set. `Caca.VM.Compiler.Compiler` is a straight
assembler with no symbol table — there's nothing for hover, go-to-definition,
or find-references to query, so this extension doesn't claim them. It also
stops at the first problem it finds rather than collecting every error in the
file, so fixing one error re-triggers a recompile that surfaces the next one,
not the full list at once.

## Without the server

The extension contributes syntax highlighting, comment rules, and bracket
matching on its own; if the language client library is missing it says so
and those keep working.

`.vscode/tasks.json` builds and runs the current file through
`Caca.VM.Cli`, and a problem matcher (`$cil`) puts any compile error into the
Problems panel in the same `file(line,col): error CODE: message` format the
CLI itself prints — so a build reports the same diagnostic whether or not the
language server is running.
