# Licenses

cacasoft bundles two projects under different licenses, plus the license
texts for their FOSS dependencies. Each file below is unmodified license
text; nothing here is project documentation (see [`/docs`](../docs) for that).

| File | SPDX Identifier | Applies to |
|------|-----------------|------------|
| [CacaVM-LICENSE](CacaVM-LICENSE) | `BSD-2-Clause-Clear` | Everything under [`/CacaVM`](../CacaVM) and [`/cacaswift`](../cacaswift) — original source code, documentation, and examples |
| [cacalang-LICENSE](cacalang-LICENSE) | `MIT` | Everything under [`/cacalang`](../cacalang) |
| [CacaVM-THIRD-PARTY-MIT.md](CacaVM-THIRD-PARTY-MIT.md) | `MIT` | .NET SDK, Microsoft.NET.Test.Sdk, xunit.runner.visualstudio (CacaVM's dependencies) |
| [CacaVM-THIRD-PARTY-APACHE-2.0.md](CacaVM-THIRD-PARTY-APACHE-2.0.md) | `Apache-2.0` | xUnit (`xunit` NuGet package, a CacaVM dependency) |
| [cacalang-vscode-LICENSE](cacalang-vscode-LICENSE) | `MIT` | The VS Code extension at `cacalang/editors/vscode` — copied into that folder at package time (see [Cacalang-Editor](../docs/Cacalang-Editor)), since `vsce package` only bundles a LICENSE it finds in the extension's own directory |
