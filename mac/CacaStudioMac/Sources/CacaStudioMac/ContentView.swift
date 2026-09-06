import SwiftUI
import CacaVMKit

private let defaultSource = """
; Caca Studio (macOS) — CIL editor
; Assemble and run with the buttons above, or Cmd-R.

JMP main
hello:
DB "Hello, World", 0x0A, 0x00
main:
MOV AL, 0x02
MOV X, hello
MOV BL, 13
KEI 0x01
KEI 0x02
"""

struct ContentView: View {
    @State private var source = defaultSource
    @State private var output = ""
    @State private var isRunning = false

    var body: some View {
        VSplitView {
            editor
            consolePane
        }
        .toolbar {
            ToolbarItemGroup {
                Button("Run", systemImage: "play.fill", action: run)
                    .disabled(isRunning)
                    .keyboardShortcut("r", modifiers: .command)
                Button("Clear Output", systemImage: "trash", action: { output = "" })
            }
        }
        .frame(minWidth: 700, minHeight: 500)
    }

    private var editor: some View {
        TextEditor(text: $source)
            .font(.system(.body, design: .monospaced))
            .frame(minHeight: 260)
    }

    private var consolePane: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("Console")
                .font(.caption)
                .foregroundStyle(.secondary)
                .padding(.horizontal, 8)
                .padding(.top, 4)
            ScrollView {
                Text(output.isEmpty ? " " : output)
                    .font(.system(.body, design: .monospaced))
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .textSelection(.enabled)
                    .padding(8)
            }
        }
        .frame(minHeight: 140)
        .background(Color(nsColor: .textBackgroundColor))
    }

    private func run() {
        isRunning = true
        output = ""
        let console = LiveConsole { text in
            DispatchQueue.main.async { output += text }
        }

        DispatchQueue.global(qos: .userInitiated).async {
            defer { DispatchQueue.main.async { isRunning = false } }
            do {
                let code = try Assembler.assemble(source)
                let vm = VM(program: code, ramSize: 1_048_576, console: console)
                try vm.run()
            } catch {
                DispatchQueue.main.async { output += "\n✗ \(error)\n" }
            }
        }
    }
}

/// Streams output to a callback as it's produced, rather than buffering it —
/// so long-running programs show output incrementally instead of all at once
/// when they halt.
private final class LiveConsole: CacaConsole {
    private let onOutput: (String) -> Void
    init(onOutput: @escaping (String) -> Void) { self.onOutput = onOutput }
    func write(_ text: String) { onOutput(text) }
    func writeLine(_ text: String) { onOutput(text + "\n") }
    func read() throws -> UInt8 { throw CacaVMError.unknownInterrupt(command: -1) }
    func readLine() throws -> String { throw CacaVMError.unknownInterrupt(command: -1) }
}

#Preview {
    ContentView()
}
