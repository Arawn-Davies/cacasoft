# CacaVM

A free and open source register-based virtual machine and intermediate language targeting .NET Standard.

CacaVM (CIL) provides a stable, portable compilation target with a small orthogonal instruction set, a two-pass assembler, a decompiler, and an integrated IDE (Caca Studio). The same CIL bytecode runs identically on any compliant VM implementation.

For the full language and VM specification, standard library reference, and instruction set, see the **[GitHub Wiki](../../wiki)**.

[cacalang](https://github.com/Arawn-Davies/cacalang) compiles to CIL as a
fourth backend (`caca build --target cacavm`), covering a v1 subset of the
language — control flow, recursion, and integer/bool arithmetic, but not yet
`float`, `extern func`, or `string` beyond a literal `print`. See its
`src/Caca.CilBackend/CilEmitter.cs` for the emitter and `docs/architecture.md`
for how it addresses locals and parameters without a frame-pointer register,
which this VM's instruction set does not leave room for.

---

## Repository layout

| Path | Contents |
|------|----------|
| `/source/Caca.VM` | Core VM library (netstandard2.0) — registers, RAM, instruction execution |
| `/source/Caca.VM.Cli` | Command-line runtime host (net10.0) |
| `/source/Caca.VM.Studio` | WinForms IDE with assembler, decompiler, and step debugger (net10.0-windows) |
| `/source/Caca.VM.Tests` | xUnit test suite |
| `/source/Caca.VM.LanguageServer` | Language Server Protocol implementation for CIL (live errors) |
| `/editors/vscode` | VS Code extension: syntax highlighting + the language server client |
| `/wiki` | Mirror of the GitHub Wiki pages |
| `/LICENSES` | License texts for this project and its FOSS dependencies |
| `/examples` | Sample `.cil` assembly programs |

---

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```sh
# Build all projects
dotnet build "CacaVM.sln"

# Run the tests
dotnet test source/Caca.VM.Tests/Caca.VM.Tests.csproj
```

> **Note:** Caca.VM.Studio targets `net10.0-windows` and will only build on Windows. The VM library, runtime, and tests build cross-platform.

---

## Running

```sh
# Run the built-in "Hello, World!" demo
dotnet run --project source/Caca.VM.Cli

# Compile and run a .cil source file directly
dotnet run --project source/Caca.VM.Cli -- path/to/program.cil

# Or run pre-compiled bytecode
dotnet run --project source/Caca.VM.Cli -- path/to/program.ilc
```

---

## Examples

The `/examples` folder contains ready-to-assemble `.cil` programs:

| File | Description |
|------|-------------|
| `hello_world_db.cil` | Prints "Hello, World" using the `DB` pseudo-instruction and the write-string interrupt |
| `calculator.cil` | Demonstrates arithmetic instructions and integer output |
| `stdlib_demo.cil` | Signed 32-bit integer output, `atoi`, and `strcmp` — the standard library's newer additions |
| `all_features.cil` | A section-by-section tour of every implemented instruction and standard library routine — the file to read alongside the wiki |

Open any `.cil` file in Caca Studio to assemble, run, and step-debug it interactively.

---

## VS Code

For editors other than Caca Studio, `/editors/vscode` provides syntax
highlighting and live compile errors via a Language Server Protocol client.
See [`editors/vscode/README.md`](editors/vscode/README.md) for setup.

---

## License

This project is released under the [Clear BSD License](LICENSE). License texts for all FOSS dependencies (xUnit, .NET SDK) are collected in the [`LICENSES/`](LICENSES/) directory.
