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
    @Environment(\.dismissWindow) private var dismissWindow

    var body: some View {
        VSplitView {
            CodeEditorView(text: $appState.source) { line, column in
                appState.statusLine = line
                appState.statusColumn = column
            }
            .onChange(of: appState.source) { appState.modified = true }
            .frame(minHeight: 260)

            outputPane
                .frame(minHeight: 140)
        }
        .navigationTitle(appState.windowTitle)
        .background(WindowAccessor { window in
            guard appState.windowCloseDelegate == nil else { return }
            let delegate = ConfirmCloseDelegate(appState: appState)
            appState.windowCloseDelegate = delegate
            window.delegate = delegate
        })
        .toolbar {
            ToolbarItemGroup {
                Button("New", systemImage: "doc") { appState.newDocument() }
                Button("Open…", systemImage: "folder") { appState.open() }
                Button("Save", systemImage: "square.and.arrow.down") { appState.save() }
                Menu("Examples", systemImage: "text.book.closed") {
                    ForEach(Example.allCases) { example in
                        Button(example.rawValue) { appState.loadExample(example) }
                    }
                }
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
            HStack {
                Text("  Output").font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button {
                    if appState.consoleUndocked {
                        dismissWindow(id: "console")
                        appState.consoleUndocked = false
                    } else {
                        appState.consoleUndocked = true
                        openWindow(id: "console")
                    }
                } label: {
                    Image(systemName: appState.consoleUndocked ? "dock.rectangle" : "rectangle.on.rectangle")
                }
                .buttonStyle(.plain)
                .help(appState.consoleUndocked ? "Dock back to main window" : "Pop out to separate window")
                .padding(.trailing, 6)
            }
            .padding(.top, 4)

            if appState.consoleUndocked {
                Spacer()
                Text("Console is popped out")
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                OutputConsoleView(segments: appState.output)
            }
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

/// The undocked console window's content — a Swift counterpart to
/// MainForm.cs's UndockConsole (a floating Form holding the same output
/// panel). Closing this window (the red button, not our own toggle) redocks
/// automatically, matching UndockConsole's FormClosing handler.
struct ConsoleWindowView: View {
    @EnvironmentObject var appState: AppState

    var body: some View {
        OutputConsoleView(segments: appState.output)
            .frame(minWidth: 400, minHeight: 200)
            .onDisappear { appState.consoleUndocked = false }
    }
}

/// Grants access to the SwiftUI window's underlying NSWindow once it
/// exists, by dropping an invisible NSView into the view hierarchy and
/// reading its `.window` after layout.
private struct WindowAccessor: NSViewRepresentable {
    let callback: (NSWindow) -> Void

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        DispatchQueue.main.async {
            if let window = view.window { callback(window) }
        }
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {}
}

/// Prompts to discard unsaved changes when the main window's close button
/// is clicked — a Swift port of MainForm.cs's OnFormClosing.
final class ConfirmCloseDelegate: NSObject, NSWindowDelegate {
    weak var appState: AppState?
    init(appState: AppState) { self.appState = appState }

    func windowShouldClose(_ sender: NSWindow) -> Bool {
        appState?.confirmDiscard() ?? true
    }
}

#Preview {
    ContentView().environmentObject(AppState())
}
