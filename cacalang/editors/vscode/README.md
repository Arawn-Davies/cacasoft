# cacalang & CIL for Visual Studio Code

One extension, two languages: [cacalang](../../README.md) (syntax
highlighting, live errors, hover types, go to definition, find references,
document outline) and CIL — the assembly language of
[CacaVM](https://github.com/Arawn-Davies/CacaVM), a separate project this
extension does not vendor (syntax highlighting, live errors).

Each language has its own server, started independently: a missing or
unconfigured CIL server does not stop cacalang's from working, and vice
versa.

## Installing

The extension is a thin client for both languages: the actual work is done
by `caca-langserver` (part of this repository) and `cil-langserver` (part of
CacaVM).

1. Build and publish cacalang's server into this repository, where the
   committed workspace settings expect it:

   ```sh
   dotnet publish src/Caca.LanguageServer -c Release -o artifacts/langserver
   ```

2. If you also want live errors for `.cil` files, clone
   [CacaVM](https://github.com/Arawn-Davies/CacaVM) and publish its server —
   anywhere, since there is no committed default path for a repository this
   one doesn't contain:

   ```sh
   dotnet publish source/Caca.VM.LanguageServer -c Release -o artifacts/langserver
   ```

   Then point VS Code at it — either set `cil.server.path` to the full path
   of the resulting `cil-langserver.dll`, or put the publish output on your
   `PATH` so the extension's `cil-langserver` fallback finds it.

3. Install the extension:

   ```sh
   cd editors/vscode
   npm install
   npx @vscode/vsce package
   code --install-extension cacalang-0.3.0.vsix
   ```

   Or, to try it without packaging, open this folder in VS Code and press F5 to
   launch an Extension Development Host.

`.vscode/settings.json` points `cacalang.server.path` at
`${workspaceFolder}/artifacts/langserver/caca-langserver.dll`, so nothing needs
to be on your `PATH` beyond `dotnet` itself. It does not set a default for
`cil.server.path` — this repository has no CIL server to point at — so that
one falls back to `cil-langserver` from `PATH` until you configure it.

`cacalang.references` lists .NET assemblies extern functions may bind to — the
editor counterpart of the CLI's `--ref`. `${workspaceFolder}` is expanded, and
the list is read when the server starts, so reload the window after changing
it or after building a listed assembly for the first time.

### Why each server is run through `dotnet`

`dotnet publish` produces both a `.dll` and a native launcher beside it. The
extension runs the `.dll` through `dotnet`. The launcher finds the runtime
through the standard install locations and `DOTNET_ROOT`, neither of which
covers a .NET installed under the user's home directory — so it fails to start
in exactly the setup where an editor is most likely to launch it, and with an
error the user never sees. `dotnet` is on `PATH`, so running the `.dll` works
wherever the compiler itself does.

## What each server provides

cacalang (`Caca.LanguageServer`):

| Feature | Request |
|---|---|
| Errors as you type | `textDocument/publishDiagnostics` |
| Hover showing a name's type | `textDocument/hover` |
| Go to definition | `textDocument/definition` |
| Find all references | `textDocument/references` |
| Outline and breadcrumbs | `textDocument/documentSymbol` |

The server compiles the whole file on every keystroke. Programs in this
language are small enough that this is imperceptible, and it means the editor
sees exactly what the compiler sees.

CIL (`Caca.VM.LanguageServer`, from CacaVM):

| Feature | Request |
|---|---|
| Errors as you type | `textDocument/publishDiagnostics` |

That's the whole feature set for CIL. `Caca.VM.Compiler.Compiler` is a
straight assembler with no symbol table — there's nothing for hover,
go-to-definition or find-references to query. It also stops at the first
problem it finds rather than collecting every error in the file, so fixing
one error re-triggers a recompile that surfaces the next one, not the full
list at once.

## Without either server

The extension contributes syntax highlighting, comment/bracket rules and
(for cacalang) indentation on its own for both languages; if the language
client library is missing it says so and those keep working.

`.vscode/tasks.json` runs, checks and builds the current `.caca` file, and a
problem matcher (`$caca`) puts any errors into the Problems panel, so a build
reports the same diagnostics whether or not cacalang's server is running.
There is no equivalent task for `.cil` files here — running and debugging
those belongs to CacaVM's own tooling (`Caca.VM.Cli`, or Caca Studio's
step debugger) — but the `$cil` problem matcher is still contributed, so a
task defined in a CacaVM checkout (or a multi-root workspace covering both
repositories) can use it.

`.vscode/launch.json` steps through a `.caca` file itself, using the symbols
`caca build` writes. That needs the C# extension (`ms-dotnettools.csharp`),
which supplies the `coreclr` debug adapter.
