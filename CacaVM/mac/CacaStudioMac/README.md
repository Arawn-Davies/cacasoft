# CacaStudioMac

A native macOS SwiftUI port of Caca Studio — the same CIL/cacalang IDE as
`source/Caca.VM.Studio` (WinForms), rebuilt from scratch against a from-
scratch Swift VM engine (`CacaVMKit`), not a wrapper around the C# one.

## Building and testing (headless, no bundle needed)

```sh
swift build   # builds CacaVMKit + the CacaStudioMac executable
swift test    # runs CacaVMKitTests, including the shared conformance
              # corpus in /conformance/cases (see its README)
```

`swift run CacaStudioMac` or running `.build/*/debug/CacaStudioMac`
directly both launch the app, but as a bare, unbundled Mach-O binary —
fine for iterating, but Finder/Spotlight/Dock/`open -a` won't recognize
it as an application, and SwiftUI Previews' Canvas won't work (that needs
an Xcode-managed build setting no plain SPM executable target has).

## Building the real .app

```sh
./package-app.sh          # release build, ad-hoc signed
./package-app.sh debug    # debug build instead
open "build/Caca Studio.app"
```

This assembles `build/Caca Studio.app` (a real `Contents/{MacOS,
Resources, Info.plist}` bundle, ad-hoc codesigned so Gatekeeper doesn't
object locally) from the plain `swift build` output — `build/` is
gitignored, same as `.build/`; rerun the script after any source change,
there's no separate "install" step.

## Xcode

`open .` (or `open Package.swift`) opens the package in Xcode for editing
and debugging. Canvas Previews don't work for the executable target for
the reason above — use `Cmd+R` or the packaged `.app` instead.
