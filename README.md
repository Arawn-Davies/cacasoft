# cacasoft

Umbrella repo bundling the whole suite into a single clone:

- **[CacaVM](https://github.com/Arawn-Davies/CacaVM)** — a register-based virtual machine (48-bit fixed-width instructions, byte-addressable memory), its WinForms and macOS/Swift IDEs (Caca Studio), and the CIL assembler/decompiler.
- **[cacalang](https://github.com/Arawn-Davies/cacalang)** — a general-purpose compiler (`caca`) with four backends: a .NET IL target, a C target (hosted and freestanding), and CacaVM's own CIL — the thing that lets Caca Studio open and step through `.caca` source, not just raw CIL.

Both are submodules, each still fully independent: their own history, issues, versioning, and either can still be cloned and built entirely on its own. The path-discovery convention both codebases already use (walk up to a directory named `CacaVM`, then look for a sibling `cacalang`, or vice versa) works unmodified here, because a submodule's checkout directory is a real sibling directory on disk — this repo adds nothing beyond the two submodule pointers and this file.

## Clone

```
git clone --recurse-submodules https://github.com/Arawn-Davies/cacasoft
```

(Already have a clone without submodules? `git submodule update --init --recursive`.)

## Build

Each submodule builds exactly as it does standalone — see `CacaVM/README.md` and `cacalang/README.md`. Building `CacaVM/source/Caca.VM.Studio` (or the macOS IDE under `CacaVM/mac/CacaStudioMac`) from inside this layout automatically picks up cacalang for `.caca` support, and cacalang's own `Caca.Tests` automatically finds a built CacaVM CLI for its CacaVM-backend parity tests — no configuration needed either direction.
