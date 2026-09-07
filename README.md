# cacasoft

Monorepo for the whole suite:

- **CacaVM** (`CacaVM/`) — a register-based virtual machine (48-bit fixed-width instructions, byte-addressable memory), its WinForms and macOS/Swift IDEs (Caca Studio), and the CIL assembler/decompiler.
- **cacalang** (`cacalang/`) — a general-purpose compiler (`caca`) with four backends: a .NET IL target, a C target (hosted and freestanding), and CacaVM's own CIL — the thing that lets Caca Studio open and step through `.caca` source, not just raw CIL.

Formerly two independent repos wired together as git submodules; both full histories are now merged into this repo (see `CacaVM/` and `cacalang/` commit history). The path-discovery convention both codebases already use (walk up to a directory named `CacaVM`, then look for a sibling `cacalang`, or vice versa) still works unmodified, since both remain real sibling directories on disk.

## Clone

```
git clone https://github.com/Arawn-Davies/cacasoft
```

## Build

`cacasoft.sln` at the repo root covers every .NET project in both `CacaVM/` and `cacalang/`, including the shared `Caca.CilBackend`:

```sh
dotnet build cacasoft.sln
```

Building `CacaVM/source/Caca.VM.Studio` (or the macOS IDE under `CacaVM/mac/CacaStudioMac`, built separately via Swift Package Manager) automatically picks up cacalang for `.caca` support, and cacalang's own `Caca.Tests` automatically finds a built CacaVM CLI for its CacaVM-backend parity tests — no configuration needed either direction.
