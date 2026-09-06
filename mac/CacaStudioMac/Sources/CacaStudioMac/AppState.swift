import AppKit
import SwiftUI
import UniformTypeIdentifiers
import CacaVMKit

/// One output line, colour-tagged the same way MainForm.cs's AppendOutput
/// tags console text (info/success/error/plain).
struct OutputSegment: Identifiable {
    let id = UUID()
    let text: String
    var kind: Kind = .plain

    enum Kind {
        case plain, info, success, error

        var color: Color {
            switch self {
            case .plain:   return Color(nsColor: .textColor)
            case .info:    return Color(red: 0x9C / 255, green: 0xDC / 255, blue: 0xFE / 255)
            case .success: return Color(red: 0x6A / 255, green: 0x99 / 255, blue: 0x55 / 255)
            case .error:   return Color(red: 0xF4 / 255, green: 0x47 / 255, blue: 0x47 / 255)
            }
        }
    }
}

/// Which language the editor's `source` text currently is. Load Example,
/// Open, and Decompile all set this; Compile/Compile & Run/Debug read it to
/// decide whether `source` can be assembled directly or needs cross-
/// compiling through cacalang first — see AppState.resolveToCIL().
enum SourceLanguage {
    case cil, cacalang
}

/// The single-document application state — a Swift port of Caca.VM.Studio's
/// MainForm, minus the WinForms control plumbing. One editor, one output
/// pane, one optional debug session, exactly like the WinForms IDE (it isn't
/// a multi-window NSDocument-based app either).
@MainActor
final class AppState: ObservableObject {
    @Published var source: String = defaultSource
    @Published var filePath: URL?
    @Published var modified = false
    @Published var lastBuild: [UInt8] = []
    @Published var output: [OutputSegment] = []
    @Published var statusLine = 1
    @Published var statusColumn = 1
    @Published var isRunning = false
    @Published var debugSession: DebugSession?
    @Published var showingAbout = false
    @Published var consoleUndocked = false

    /// Set while Compile & Run's console is blocked waiting for stdin —
    /// the output pane shows an input field exactly while this is true.
    @Published var isWaitingForInput = false
    private var pendingConsole: LiveConsole?

    /// Called by the output pane's input field on submit.
    func submitConsoleInput(_ text: String) {
        pendingConsole?.provideLine(text)
        isWaitingForInput = false
    }

    /// Which language `source` currently holds. Load Example (either
    /// language), Open (by extension), and Decompile (always CIL) all set
    /// this. Compile/Compile & Run/Debug read it via resolveToCIL() rather
    /// than assuming `source` is always directly-assemblable CIL — cacalang
    /// source is shown and edited as-is, never silently replaced with its
    /// compiled CIL the moment it's loaded.
    @Published var language: SourceLanguage = .cil

    /// Set by loadExample — there's no file on disk to name the document
    /// after, so without this the title bar falls through to "Untitled"
    /// for every example, indistinguishable from a genuinely blank
    /// document. Cleared by anything that gives the document a real
    /// identity instead (New, Open, Decompile).
    @Published var loadedExampleName: String?

    /// Kept alive here so ARC doesn't drop it the moment WindowAccessor's
    /// closure returns — see ConfirmCloseDelegate in ContentView.swift.
    var windowCloseDelegate: NSObject?

    // MARK: - Output helpers

    func clearOutput() { output.removeAll() }

    func appendOutput(_ text: String, _ kind: OutputSegment.Kind = .plain) {
        output.append(OutputSegment(text: text, kind: kind))
    }

    // MARK: - Title / status, mirrors MainForm.SetTitle

    var windowTitle: String {
        let mark = modified ? "● " : ""
        if let filePath {
            return mark + filePath.lastPathComponent
        }
        if let loadedExampleName {
            let tag = language == .cacalang ? "cacalang example" : "example"
            return mark + loadedExampleName + " (\(tag))"
        }
        return mark + "Untitled"
    }

    var statusFileText: String {
        if let filePath {
            return filePath.path
        }
        if let loadedExampleName {
            return "example: \(loadedExampleName)"
        }
        return "New file"
    }

    // MARK: - File operations, mirrors MainForm's New/Open/Save/SaveAs

    func confirmDiscard() -> Bool {
        guard modified else { return true }
        let alert = NSAlert()
        alert.messageText = "Caca Studio"
        alert.informativeText = "You have unsaved changes. Discard them?"
        alert.addButton(withTitle: "Discard")
        alert.addButton(withTitle: "Cancel")
        alert.alertStyle = .warning
        return alert.runModal() == .alertFirstButtonReturn
    }

    func newDocument() {
        guard confirmDiscard() else { return }
        source = defaultSource
        filePath = nil
        language = .cil
        loadedExampleName = nil
        modified = false
        lastBuild = []
        clearOutput()
    }

    func open() {
        guard confirmDiscard() else { return }
        let panel = NSOpenPanel()
        panel.title = "Open Source"
        panel.allowedContentTypes = CacalangCompiler.findCli() != nil
            ? cilContentTypes() + [UTType(filenameExtension: "caca")].compactMap { $0 }
            : cilContentTypes()
        panel.allowsOtherFileTypes = true
        guard panel.runModal() == .OK, let url = panel.url else { return }

        do {
            source = try String(contentsOf: url, encoding: .utf8)
            filePath = url
            language = url.pathExtension.lowercased() == "caca" ? .cacalang : .cil
            loadedExampleName = nil
            modified = false
            lastBuild = []
            clearOutput()
        } catch {
            appendOutput("✗ Could not open \(url.path): \(error.localizedDescription)\n", .error)
        }
    }

    func save() {
        guard let filePath else { saveAs(); return }
        writeSource(to: filePath)
    }

    func saveAs() {
        let panel = NSSavePanel()
        switch language {
        case .cil:
            panel.title = "Save CIL Source"
            panel.allowedContentTypes = cilContentTypes()
            panel.nameFieldStringValue = filePath?.lastPathComponent ?? "program.cil"
        case .cacalang:
            panel.title = "Save cacalang Source"
            panel.allowedContentTypes = [UTType(filenameExtension: "caca")].compactMap { $0 }
            panel.nameFieldStringValue = filePath?.lastPathComponent ?? "program.caca"
        }
        guard panel.runModal() == .OK, let url = panel.url else { return }
        writeSource(to: url)
    }

    private func writeSource(to url: URL) {
        do {
            try source.write(to: url, atomically: true, encoding: .utf8)
            filePath = url
            modified = false
        } catch {
            appendOutput("✗ Could not save \(url.path): \(error.localizedDescription)\n", .error)
        }
    }

    func loadExample(_ example: Example) {
        guard confirmDiscard() else { return }
        filePath = nil
        language = .cil
        lastBuild = []
        clearOutput()
        source = example.source
        modified = false
        loadedExampleName = example.rawValue
    }

    /// Loads one of cacalang's own bundled samples' RAW SOURCE — not
    /// compiled to CIL. It's shown and edited as cacalang; Compile/
    /// Compile & Run/Debug cross-compile it on demand via resolveToCIL().
    /// See CacalangExample and CacalangCompiler.findSamplesDir.
    func loadCacalangExample(_ example: CacalangExample) {
        guard confirmDiscard() else { return }

        guard let samplesDir = CacalangCompiler.findSamplesDir() else {
            clearOutput()
            appendOutput("cacalang samples not found — check out a sibling ../cacalang next to this repo.\n", .error)
            return
        }

        let path = samplesDir.appendingPathComponent(example.fileName + ".caca")
        do {
            source = try String(contentsOf: path, encoding: .utf8)
        } catch {
            clearOutput()
            appendOutput("cacalang sample not found: \(path.path)\n", .error)
            return
        }

        filePath = nil
        language = .cacalang
        lastBuild = []
        clearOutput()
        modified = false
        loadedExampleName = example.rawValue
    }

    /// Cross-compiles `source` to CIL via cacalang's own CLI when the
    /// editor holds cacalang source, or returns it unchanged when it's
    /// already CIL. Compile/Compile & Run/Debug all go through this rather
    /// than assuming `source` is always directly-assemblable. Appends
    /// compile diagnostics to output on failure and returns nil; callers
    /// should already have called clearOutput() before this.
    private func resolveToCIL() -> String? {
        switch language {
        case .cil:
            return source
        case .cacalang:
            appendOutput("── Compiling cacalang → CIL ──────────\n", .info)
            let tempURL = FileManager.default.temporaryDirectory
                .appendingPathComponent("cacastudio-\(UUID().uuidString).caca")
            do {
                try source.write(to: tempURL, atomically: true, encoding: .utf8)
            } catch {
                appendOutput("✗ Could not write temporary cacalang source: \(error.localizedDescription)\n", .error)
                return nil
            }
            defer { try? FileManager.default.removeItem(at: tempURL) }

            let result = CacalangCompiler.compile(tempURL)
            if let diagnostics = result.diagnostics {
                appendOutput(diagnostics, .error)
                if !diagnostics.hasSuffix("\n") { appendOutput("\n") }
                return nil
            }
            appendOutput("✓ Compiled to CIL.\n", .success)
            return result.cilSource
        }
    }

    private func cilContentTypes() -> [UTType] {
        [UTType(filenameExtension: "cil"), UTType(filenameExtension: "asm")].compactMap { $0 }
    }

    // MARK: - Build / run, mirrors MainForm's Compile/CompileAndRun/OpenDebugger/OpenAndDecompile

    func compile() {
        clearOutput()
        guard let cil = resolveToCIL() else { return }
        appendOutput("── Compiling… ──────────────────────\n", .info)
        do {
            let code = try Assembler.assemble(cil)
            lastBuild = code
            appendOutput("✓ Compiled OK — \(code.count / 6) instruction(s), \(code.count) byte(s).\n", .success)

            let panel = NSSavePanel()
            panel.title = "Save compiled binary"
            panel.allowedContentTypes = [UTType(filenameExtension: "ilc")].compactMap { $0 }
            panel.nameFieldStringValue = (filePath?.deletingPathExtension().lastPathComponent ?? "output") + ".ilc"
            if panel.runModal() == .OK, let url = panel.url {
                let ilc = IlcContainer.wrap(code)
                do {
                    try Data(ilc).write(to: url)
                    appendOutput("  Saved → \(url.path)\n", .info)
                } catch {
                    appendOutput("✗ Could not save \(url.path): \(error.localizedDescription)\n", .error)
                }
            }
        } catch {
            appendOutput("✗ \(error)\n", .error)
        }
    }

    func compileAndRun() {
        clearOutput()
        guard let cil = resolveToCIL() else { return }
        appendOutput("── Compile & Run ────────────────────\n", .info)
        let code: [UInt8]
        do {
            code = try Assembler.assemble(cil)
            lastBuild = code
            appendOutput("✓ Compiled — \(code.count / 6) instruction(s).\n", .success)
            appendOutput("── VM output ────────────────────────\n", .info)
        } catch {
            appendOutput("✗ \(error)\n", .error)
            return
        }

        isRunning = true
        let console = LiveConsole { [weak self] text in
            Task { @MainActor in self?.appendOutput(text) }
        }
        console.onNeedInput = { [weak self, weak console] in
            self?.pendingConsole = console
            self?.isWaitingForInput = true
        }

        Task.detached(priority: .userInitiated) { [weak self] in
            defer { Task { @MainActor in self?.isRunning = false; self?.isWaitingForInput = false; self?.pendingConsole = nil } }
            do {
                let vm = VM(program: code, ramSize: 1_048_576, console: console)
                try vm.run()
                await MainActor.run { [weak self] in self?.appendOutput("\n── Done ─────────────────────────────\n", .info) }
            } catch {
                await MainActor.run { [weak self] in self?.appendOutput("\n✗ Runtime error: \(error)\n", .error) }
            }
        }
    }

    func openDebugger(openWindow: OpenWindowAction) {
        clearOutput()
        guard let cil = resolveToCIL() else { return }
        appendOutput("── Debug ────────────────────────────\n", .info)
        do {
            let code = try Assembler.assemble(cil)
            appendOutput("✓ Compiled — \(code.count / 6) instruction(s).\n", .success)
            debugSession = DebugSession(code: code)
            openWindow(id: "debugger")
        } catch {
            appendOutput("✗ \(error)\n", .error)
        }
    }

    func openAndDecompile() {
        let panel = NSOpenPanel()
        panel.title = "Open .ilc binary to decompile"
        panel.allowedContentTypes = [UTType(filenameExtension: "ilc")].compactMap { $0 }
        panel.allowsOtherFileTypes = true
        guard panel.runModal() == .OK, let url = panel.url else { return }
        guard confirmDiscard() else { return }

        do {
            let bytes = [UInt8](try Data(contentsOf: url))
            let asm = try Decompiler.decompile(bytes)
            source = asm
            filePath = nil
            language = .cil
            loadedExampleName = nil
            modified = false
            clearOutput()
            appendOutput("✓ Decompiled \(bytes.count) byte(s) from \(url.lastPathComponent)\n", .success)
        } catch {
            let alert = NSAlert()
            alert.messageText = "Decompile Error"
            alert.informativeText = "\(error)"
            alert.alertStyle = .critical
            alert.runModal()
        }
    }
}

/// Streams output to a callback as it's produced, rather than buffering it —
/// so long-running programs show output incrementally instead of all at once
/// when they halt. Also supports interactive input: the VM always runs on a
/// background thread (vm.run()/vm.tick() are synchronous, blocking calls), so
/// read()/readLine() block that thread on a semaphore until the UI thread
/// calls provideLine(_:) — the real fix for programs like FizzBuzz that ask
/// a question via stdin before doing anything else: read()/readLine()
/// previously just threw unconditionally, so any program that reads input
/// failed immediately, before printing a single line, regardless of the
/// program or the VM being otherwise completely correct.
final class LiveConsole: CacaConsole, @unchecked Sendable {
    var onOutput: (String) -> Void
    /// Called (already hopped to the main actor) when a read is blocking,
    /// so the UI can show a prompt. Not called again until the current
    /// request is satisfied by provideLine(_:).
    var onNeedInput: (() -> Void)?

    private let semaphore = DispatchSemaphore(value: 0)
    private var pendingLine = ""

    init(onOutput: @escaping (String) -> Void) { self.onOutput = onOutput }
    func write(_ text: String) { onOutput(text) }
    func writeLine(_ text: String) { onOutput(text + "\n") }

    /// Called from the UI thread once the user submits a line.
    func provideLine(_ line: String) {
        pendingLine = line
        semaphore.signal()
    }

    func read() throws -> UInt8 {
        Task { @MainActor in self.onNeedInput?() }
        semaphore.wait()
        return pendingLine.utf8.first ?? 0
    }

    func readLine() throws -> String {
        Task { @MainActor in self.onNeedInput?() }
        semaphore.wait()
        return pendingLine
    }
}
