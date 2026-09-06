import SwiftUI
import CacaVMKit

/// The main IDE window — a SwiftUI port of Caca.VM.Studio's MainForm:
/// toolbar, line-numbered/syntax-highlighted editor, colour-coded output
/// pane, and a status bar. File/Build menu commands live in
/// CacaStudioMacApp's `.commands`; this view just renders the document and
/// exposes the same actions via toolbar buttons.
struct ContentView: View {
    @EnvironmentObject var appState: AppState
    @Environment(\.openWindow) private var openWindow

    var body: some View {
        VSplitView {
            CodeEditorView(text: $appState.source) { line, column in
                appState.statusLine = line
                appState.statusColumn = column
            }
            .onChange(of: appState.source) { _ in appState.modified = true }
            .frame(minHeight: 260)

            outputPane
                .frame(minHeight: 140)
        }
        .toolbar {
            ToolbarItemGroup {
                Button("New", systemImage: "doc") { appState.newDocument() }
                Button("Open…", systemImage: "folder") { appState.open() }
                Button("Save", systemImage: "square.and.arrow.down") { appState.save() }
                Divider()
                Button("Compile", systemImage: "hammer") { appState.compile() }
                    .disabled(appState.isRunning)
                Button("Compile & Run", systemImage: "play.fill") { appState.compileAndRun() }
                    .disabled(appState.isRunning)
                Button("Debug", systemImage: "ladybug") { appState.openDebugger(openWindow: openWindow) }
                    .disabled(appState.isRunning)
                Divider()
                Button("Decompile…", systemImage: "doc.text.magnifyingglass") { appState.openAndDecompile() }
                Divider()
                Button("About", systemImage: "info.circle") { appState.showingAbout = true }
            }
        }
        .safeAreaInset(edge: .bottom, spacing: 0) {
            statusBar
        }
        .sheet(isPresented: $appState.showingAbout) { AboutView() }
        .frame(minWidth: 900, minHeight: 640)
    }

    private var outputPane: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("  Output")
                .font(.caption)
                .foregroundStyle(.secondary)
                .padding(.top, 4)
            OutputConsoleView(segments: appState.output)
        }
        .background(Color(nsColor: .init(red: 0x25 / 255, green: 0x25 / 255, blue: 0x26 / 255, alpha: 1)))
    }

    private var statusBar: some View {
        HStack {
            Text(appState.statusFileText).font(.caption).foregroundStyle(.secondary)
            Spacer()
            Text("Ln \(appState.statusLine), Col \(appState.statusColumn)")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 3)
        .background(.thinMaterial)
    }
}

private struct AboutView: View {
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        VStack(spacing: 12) {
            Text("Caca Studio").font(.title2).bold()
            Text("IDE for Caca Intermediate Language & cacalang\nAssembler · Decompiler · Debugger")
                .multilineTextAlignment(.center)
                .foregroundStyle(.secondary)
            Text("Swift · SwiftUI · No external dependencies")
                .font(.caption)
                .foregroundStyle(.secondary)
            Button("OK") { dismiss() }
                .keyboardShortcut(.defaultAction)
                .padding(.top, 8)
        }
        .padding(24)
        .frame(width: 360)
    }
}

#Preview {
    ContentView().environmentObject(AppState())
}
