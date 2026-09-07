# CIL conformance suite

A corpus of test cases, at `CacaVM/conformance/cases`, shared across every
CacaVM engine implementation — currently the C# reference implementation
(`CacaVM/source/Caca.VM`) and the Swift port
(`cacaswift/Sources/CacaVMKit`) — expressed purely in terms of observable
behavior (stdout and errors), not internal register or memory state, so any
conforming engine can be checked against the same corpus via a thin,
engine-specific adapter. `CacaVM/source/Caca.VM.Tests` and
`cacaswift/Tests/CacaVMKitTests` each have one.

Existing hand-written unit tests in each engine's own suite are unaffected —
this supplements them, it does not replace them. New cross-engine regression
cases belong here; a case that only makes sense for one engine (e.g. the
decompiler, which the Swift port doesn't have) stays in that engine's own
suite.

## Format

Each subdirectory of `CacaVM/conformance/cases/` is one test case:

| File | Required? | Contents |
|---|---|---|
| `source.cil` | always | The program to assemble and run. |
| `stdin.txt` | optional | Lines fed to KEI 0x01 AL=0x03/0x04 reads, one per line. |
| `ramsize.txt` | optional | A single integer overriding the default RAM size (1,048,576 bytes, matching `Globals.DefaultRamSize`). Only for cases exercising a RAM boundary. |
| `stdout.txt` | one of these two | Exact expected output, including the VM's own trailing `Halting!\n` on a normal run to completion. |
| `error.txt` | one of these two | A substring the thrown error's message must contain. |

Exactly one of `stdout.txt` / `error.txt` is present — never both, never
neither.

## Adding a case

Run the program through one engine, confirm the output is actually correct
(not just "what came out"), save it as `stdout.txt`/`error.txt`, then run
the *other* engine against the same case before committing — a case only
counts once both engines agree on it.
