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

    // MARK: - Output helpers

    func clearOutput() { output.removeAll() }

    func appendOutput(_ text: String, _ kind: OutputSegment.Kind = .plain) {
        output.append(OutputSegment(text: text, kind: kind))
    }

    // MARK: - Title / status, mirrors MainForm.SetTitle

    var windowTitle: String {
        let name = filePath?.lastPathComponent ?? "Untitled"
        return (modified ? "● " : "") + name
    }

    var statusFileText: String {
        filePath?.path ?? "New file"
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
        modified = false
        lastBuild = []
        clearOutput()
    }

    func open() {
        guard confirmDiscard() else { return }
        let panel = NSOpenPanel()
        panel.title = "Open Source"
        panel.allowedContentTypes = cilContentTypes()
        panel.allowsOtherFileTypes = true
        guard panel.runModal() == .OK, let url = panel.url else { return }

        do {
            source = try String(contentsOf: url, encoding: .utf8)
            filePath = url
            modified = false
            lastBuild = []
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
        panel.title = "Save CIL Source"
        panel.allowedContentTypes = cilContentTypes()
        panel.nameFieldStringValue = filePath?.lastPathComponent ?? "program.cil"
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
        lastBuild = []
        clearOutput()
        source = example.source
        modified = false
    }

    private func cilContentTypes() -> [UTType] {
        [UTType(filenameExtension: "cil"), UTType(filenameExtension: "asm")].compactMap { $0 }
    }

    // MARK: - Build / run, mirrors MainForm's Compile/CompileAndRun/OpenDebugger/OpenAndDecompile

    func compile() {
        clearOutput()
        appendOutput("── Compiling… ──────────────────────\n", .info)
        do {
            let code = try Assembler.assemble(source)
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
        appendOutput("── Compile & Run ────────────────────\n", .info)
        let code: [UInt8]
        do {
            code = try Assembler.assemble(source)
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

        Task.detached(priority: .userInitiated) { [weak self] in
            defer { Task { @MainActor in self?.isRunning = false } }
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
        appendOutput("── Debug ────────────────────────────\n", .info)
        do {
            let code = try Assembler.assemble(source)
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
/// when they halt.
final class LiveConsole: CacaConsole, @unchecked Sendable {
    var onOutput: (String) -> Void
    init(onOutput: @escaping (String) -> Void) { self.onOutput = onOutput }
    func write(_ text: String) { onOutput(text) }
    func writeLine(_ text: String) { onOutput(text + "\n") }
    func read() throws -> UInt8 { throw CacaVMError.unknownInterrupt(command: -1) }
    func readLine() throws -> String { throw CacaVMError.unknownInterrupt(command: -1) }
}
