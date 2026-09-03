# Artemis Intermediate Language

Welcome to the AIL wiki. This wiki is the normative reference for the Artemis Intermediate Language virtual machine, its instruction set, standard library, and executable format.

## Pages

| Page | Description |
|------|-------------|
| [[Specification]] | AIL v2.0 specification overview and table of contents |
| [[Spec-Architecture]] | Memory model, instruction encoding, registers, and the stack (§1–§4) |
| [[Spec-Instructions]] | Program flow, full instruction reference, and opcode quick-reference table (§5–§7) |
| [[Spec-Executable-Format]] | Binary `.ila` executable file format (§8) |
| [[Standard-Library]] | Kernel and software interrupt reference |

## What is AIL?

The Artemis Intermediate Language (AIL) is a low-level, register-based intermediate language designed for deterministic, portable execution across virtual machine implementations. It provides a stable compilation target independent of the underlying host architecture.

**Design goals:**

- **Platform independence** — the same AIL bytecode runs identically on any compliant VM
- **Simplicity** — a small, orthogonal instruction set that is straightforward to implement
- **Portability** — suitable for implementation in managed runtimes (.NET/CLR), native code (C/C++), and constrained hardware (e.g. Z80/CP/M)
- **Determinism** — no undefined behaviour; all edge cases are specified

## Repository layout

```
/source   — VM and runtime source code (.NET)
/docs     — original reference documents
/wiki     — this wiki (mirror kept in the repo)
/examples — sample .ail assembly programs
```

| Project | Description |
|---------|-------------|
| `source/Artemis-VM` | Core VM library — netstandard2.0, usable from any .NET host |
| `source/AIL-Runtime` | Command-line runtime — load and execute `.ila` files or raw bytecode |
| `source/AIL-Studio` | WinForms IDE — assembler, decompiler, and step-through debugger (Windows only) |
| `source/AIL-Tests` | xUnit test suite covering the VM, assembler, and decompiler |

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows (for AIL Studio only; the VM library and runtime are cross-platform)

### Build

```sh
dotnet build "Artemis IL.sln"
```

### Run the tests

```sh
dotnet test source/AIL-Tests/AIL-Tests.csproj
```

### Run the demo

```sh
dotnet run --project source/AIL-Runtime
```

Prints "Hello, World!" using the built-in demo program encoded directly as AIL bytecode.

### Run a `.ail` file

1. Open AIL Studio (`source/AIL-Studio`) on Windows.
2. Open or type your AIL assembly source.
3. Press **Build & Run** to assemble and execute, or **Debug** to step through instructions.

Alternatively, assemble with AIL Studio and then pass the output `.ila` file to the runtime:

```sh
dotnet run --project source/AIL-Runtime -- myprogram.ila
```














## Source code

Source code is kept in the `/source/` folder. The `/wiki/` folder in this repository is a mirror of the wiki pages.

