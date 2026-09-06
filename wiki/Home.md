# Caca Intermediate Language

Welcome to the CIL wiki. This wiki is the normative reference for the Caca Intermediate Language virtual machine, its instruction set, standard library, and executable format.

## Pages

| Page | Description |
|------|-------------|
| [[Specification]] | CIL v2.1 specification overview and table of contents |
| [[Spec-Architecture]] | Memory model, instruction encoding, registers, and the stack (§1–§4) |
| [[Spec-Instructions]] | Program flow, full instruction reference, and opcode quick-reference table (§5–§7) |
| [[Spec-Executable-Format]] | Binary `.ilc` executable file format (§8) |
| [[Standard-Library]] | Kernel and software interrupt reference |

## What is CIL?

The Caca Intermediate Language (CIL) is a low-level, register-based intermediate language designed for deterministic, portable execution across virtual machine implementations. It provides a stable compilation target independent of the underlying host architecture.

**Design goals:**

- **Platform independence** — the same CIL bytecode runs identically on any compliant VM
- **Simplicity** — a small, orthogonal instruction set that is straightforward to implement
- **Portability** — suitable for implementation in managed runtimes (.NET/CLR), native code (C/C++), and constrained hardware (e.g. Z80/CP/M)
- **Determinism** — no undefined behaviour; all edge cases are specified

## Languages that target CIL

[cacalang](https://github.com/Arawn-Davies/cacalang) is the first: `caca
build --target cacavm` compiles a v1 subset of the language (control flow,
recursion, integer/bool arithmetic — not yet `float`, `extern func`, or
`string` beyond a literal `print`) to CIL assembly source, runnable with
`Caca.VM.Cli` like any other `.cil` program. Its emitter had to solve a real
problem this VM's register file creates: with only two registers (`X`, `Y`)
wide enough for a 32-bit value and no register to spare as a frame pointer,
locals and parameters are addressed relative to the current stack pointer
using a compile-time-tracked depth, not a frame pointer in a register or in
memory — the latter is a dead end on this VM specifically, since a fixed
compile-time address is read-only once the program is running.

## Repository layout

```
/source   — VM and runtime source code (.NET)
/docs     — original reference documents
/wiki     — this wiki (mirror kept in the repo)
/examples — sample .cil assembly programs
```

| Project | Description |
|---------|-------------|
| `source/Caca.VM` | Core VM library — netstandard2.0, usable from any .NET host |
| `source/Caca.VM.Cli` | Command-line runtime — compiles and runs `.cil` source directly, or loads pre-compiled bytecode |
| `source/Caca.VM.Studio` | WinForms IDE — assembler, decompiler, and step-through debugger (Windows only) |
| `source/Caca.VM.Tests` | xUnit test suite covering the VM, assembler, and decompiler |
| `source/Caca.VM.LanguageServer` | Language Server Protocol implementation for CIL (live errors) |
| `editors/vscode` | VS Code extension: syntax highlighting + the language server client |

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows (for Caca Studio only; the VM library and runtime are cross-platform)

### Build

```sh
dotnet build "CacaVM.sln"
```

### Run the tests

```sh
dotnet test source/Caca.VM.Tests/Caca.VM.Tests.csproj
```

### Run the demo

```sh
dotnet run --project source/Caca.VM.Cli
```

Prints "Hello, World!" using the built-in demo program encoded directly as CIL bytecode.

### Run a `.cil` file

The command-line runtime compiles and runs `.cil` source directly — no separate assemble step needed:

```sh
dotnet run --project source/Caca.VM.Cli -- myprogram.cil
```

A compile error is reported in `file(line,col): error CIL001: message` format and exits non-zero; anything else is loaded and executed as pre-compiled bytecode instead.

On Windows, Caca Studio (`source/Caca.VM.Studio`) additionally provides a full IDE: open or type your CIL assembly source, press **Build & Run** to assemble and execute, or **Debug** to step through instructions.

### Editing in VS Code

For editors other than Caca Studio, `editors/vscode` provides syntax
highlighting and live compile errors (no hover/definition/references —
the assembler has no symbol table to query). See
[`editors/vscode/README.md`](https://github.com/Arawn-Davies/CacaVM/blob/main/editors/vscode/README.md)
for setup.














## Source code

Source code is kept in the `/source/` folder. The `/wiki/` folder in this repository is a mirror of the wiki pages.

