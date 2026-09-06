# CacaVM Documentation

This site mirrors the [GitHub Wiki](wiki/Home) for the Caca Intermediate Language project.

## Contents

| Page | Description |
|------|-------------|
| [Home / Getting Started](wiki/Home) | Project overview, build instructions, and quick-start guide |
| [Specification](wiki/Specification) | CIL v2.1 architecture overview and links to section pages |
| [Architecture](wiki/Spec-Architecture) | Memory model, instruction encoding, registers, and the stack (§1–§4) |
| [Instructions](wiki/Spec-Instructions) | Program flow, full instruction reference, and quick-reference table (§5–§7) |
| [Executable Format](wiki/Spec-Executable-Format) | Binary `.ilc` file format (§8) |
| [Standard Library](wiki/Standard-Library) | Kernel (`KEI`) and software (`SWI`) interrupt reference with examples |

## Source code

Source code is kept in the `/source/` folder. The `/wiki/` folder in this repository is a mirror of the wiki pages.

| Project | Description |
|---------|-------------|
| `source/Caca.VM` | Core VM library — netstandard2.0, usable from any .NET host |
| `source/Caca.VM.Cli` | Command-line runtime — compiles and runs `.cil` source directly, or loads pre-compiled bytecode |
| `source/Caca.VM.Studio` | WinForms IDE — assembler, decompiler, and step-through debugger (Windows only) |
| `source/Caca.VM.Tests` | xUnit test suite covering the VM, assembler, and decompiler |
| `source/Caca.VM.LanguageServer` | Language Server Protocol implementation for CIL (live errors) — client lives in [cacalang's VS Code extension](https://github.com/Arawn-Davies/cacalang/tree/main/editors/vscode) |
