import Foundation

/// Optional cacalang integration — the Swift counterpart to Caca.VM.Studio's
/// `#if CACALANG_SUPPORT` (MainForm.cs's OpenCacalang). There's no Swift port
/// of cacalang's compiler, and porting one is real, separate feature work
/// (see docs/architecture.md, "Caca Studio and cacalang" for why source-level
/// debugging isn't attempted either way) — so instead of a compile-time
/// project reference, this shells out to cacalang's own CLI, discovered at
/// runtime via the same sibling-checkout convention used everywhere else in
/// this session: an env var override, else a `../cacalang` checkout next to
/// this repo with a built `caca` CLI. Absent either, `.caca` support is
/// simply not offered — a runtime version of the same conditional the C#
/// side expresses at compile time.
enum CacalangCompiler {
    struct Result {
        var cilSource: String?
        var diagnostics: String?
    }

    /// Locates cacalang's own bundled samples directory (github.com/Arawn-
    /// Davies/cacalang, `samples/*.caca`) — the same files cacalang's own
    /// test suite already verifies against `--target cacavm`, reused here
    /// as Load Example entries rather than separately-maintained copies
    /// that could silently drift out of sync with the compiler.
    static func findSamplesDir() -> URL? {
        var dir = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        while dir.pathComponents.count > 1 {
            if dir.lastPathComponent == "Artemis-IL" {
                let candidate = dir.deletingLastPathComponent()
                    .appendingPathComponent("cacalang/samples")
                var isDirectory: ObjCBool = false
                let exists = FileManager.default.fileExists(atPath: candidate.path, isDirectory: &isDirectory)
                return (exists && isDirectory.boolValue) ? candidate : nil
            }
            dir = dir.deletingLastPathComponent()
        }
        return nil
    }

    /// Locates cacalang's built CLI assembly (named `caca.dll`, from the
    /// `Caca.Cli` project). Checked lazily, once, since this only matters
    /// the moment someone opens a `.caca` file.
    static func findCli() -> URL? {
        if let overridePath = ProcessInfo.processInfo.environment["CACALANG_CLI"] {
            let url = URL(fileURLWithPath: overridePath)
            return FileManager.default.fileExists(atPath: url.path) ? url : nil
        }

        // Walk up from this source file to the repo root (Artemis-IL), then
        // look for a sibling `cacalang` checkout's built CLI.
        var dir = URL(fileURLWithPath: #filePath).deletingLastPathComponent()
        while dir.pathComponents.count > 1 {
            if dir.lastPathComponent == "Artemis-IL" {
                let cacalangRoot = dir.deletingLastPathComponent().appendingPathComponent("cacalang")
                let candidates = [
                    cacalangRoot.appendingPathComponent("src/Caca.Cli/bin/Debug/net10.0/caca.dll"),
                    cacalangRoot.appendingPathComponent("src/Caca.Cli/bin/Release/net10.0/caca.dll"),
                ]
                return candidates.first { FileManager.default.fileExists(atPath: $0.path) }
            }
            dir = dir.deletingLastPathComponent()
        }
        return nil
    }

    /// Compiles a `.caca` file to CIL via `dotnet <caca.dll> build --target
    /// cacavm <path> -o <tempfile>`, mirroring BuildCacaVm in cacalang's own
    /// Program.cs (writes the .cil text on success; diagnostics to stderr
    /// with a non-zero exit otherwise).
    static func compile(_ sourcePath: URL) -> Result {
        guard let cli = findCli() else {
            return Result(cilSource: nil, diagnostics:
                "cacalang CLI not found — set CACALANG_CLI to its caca.dll, " +
                "or check out a sibling ../cacalang with `dotnet build src/Caca.Cli`.")
        }

        let tempOutput = FileManager.default.temporaryDirectory
            .appendingPathComponent("cacastudio-\(UUID().uuidString).cil")
        defer { try? FileManager.default.removeItem(at: tempOutput) }

        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/env")
        process.arguments = ["dotnet", cli.path, "build", "--target", "cacavm", sourcePath.path, "-o", tempOutput.path]
        let stderrPipe = Pipe()
        process.standardError = stderrPipe
        process.standardOutput = Pipe()

        do {
            try process.run()
            process.waitUntilExit()
        } catch {
            return Result(cilSource: nil, diagnostics: "Could not launch dotnet: \(error.localizedDescription)")
        }

        if process.terminationStatus == 0, let cil = try? String(contentsOf: tempOutput, encoding: .utf8) {
            return Result(cilSource: cil, diagnostics: nil)
        }

        let errData = stderrPipe.fileHandleForReading.readDataToEndOfFile()
        let errText = String(data: errData, encoding: .utf8).flatMap { $0.isEmpty ? nil : $0 }
            ?? "cacalang build failed (exit \(process.terminationStatus))."
        return Result(cilSource: nil, diagnostics: errText)
    }
}
