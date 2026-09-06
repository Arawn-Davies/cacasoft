import Foundation

/// Optional cacalang integration — the Swift counterpart to Caca.VM.Studio's
/// `#if CACALANG_SUPPORT` (MainForm.cs's ResolveToCil). There's no Swift port
/// of cacalang's compiler, so instead of a compile-time project reference,
/// this shells out to cacalang's own CLI, discovered at runtime via the same
/// sibling-checkout convention used everywhere else in this session: an env
/// var override, else a `../cacalang` checkout next to this repo with a
/// built `caca` CLI. Absent either, `.caca` support is simply not offered —
/// a runtime version of the same conditional the C# side expresses at
/// compile time. Source-level debugging (the CIL instruction currently
/// executing shown alongside its cacalang source line) works from this
/// runtime CLI boundary too, via the `<output>.linemap` sidecar `compile`
/// reads below — see `Result.lineMap` and DebugSession.currentSourceLine.
enum CacalangCompiler {
    struct Result {
        var cilSource: String?
        var diagnostics: String?
        /// (byte offset, 1-based cacalang source line) pairs, ascending by
        /// offset — read from the `<output>.linemap` sidecar `build --target
        /// cacavm` writes alongside the .cil text (see cacalang's
        /// Caca.Cli/Program.cs). Empty on a failed compile, or if the CLI is
        /// too old to write the sidecar at all — a debugger with no map just
        /// shows CIL alone, the same as debugging a .cil file directly.
        var lineMap: [(byteOffset: Int, sourceLine: Int)] = []
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
        let tempLineMap = URL(fileURLWithPath: tempOutput.path + ".linemap")
        defer {
            try? FileManager.default.removeItem(at: tempOutput)
            try? FileManager.default.removeItem(at: tempLineMap)
        }

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
            return Result(cilSource: cil, diagnostics: nil, lineMap: readLineMap(tempLineMap))
        }

        let errData = stderrPipe.fileHandleForReading.readDataToEndOfFile()
        let errText = String(data: errData, encoding: .utf8).flatMap { $0.isEmpty ? nil : $0 }
            ?? "cacalang build failed (exit \(process.terminationStatus))."
        return Result(cilSource: nil, diagnostics: errText)
    }

    /// Parses the "<byteOffset> <sourceLine>" sidecar `build --target
    /// cacavm` writes (see cacalang's Caca.Cli/Program.cs's BuildCacaVm) —
    /// one pair per line, ascending byte offset. Missing file or any
    /// malformed line just yields an empty map (an older cacalang CLI built
    /// before this sidecar existed shouldn't break debugging, only degrade
    /// it to CIL-only, same as debugging a .cil file directly).
    private static func readLineMap(_ path: URL) -> [(byteOffset: Int, sourceLine: Int)] {
        guard let text = try? String(contentsOf: path, encoding: .utf8) else { return [] }
        return text.split(separator: "\n").compactMap { line in
            let parts = line.split(separator: " ")
            guard parts.count == 2, let offset = Int(parts[0]), let sourceLine = Int(parts[1]) else { return nil }
            return (byteOffset: offset, sourceLine: sourceLine)
        }
    }
}
