import SwiftUI

@main
struct CacaStudioMacApp: App {
    @StateObject private var appState = AppState()
    @Environment(\.openWindow) private var openWindow

    var body: some Scene {
        WindowGroup {
            RootView()
                .environmentObject(appState)
        }
        .commands {
            CommandGroup(replacing: .newItem) {
                Button("New") { appState.newDocument() }
                    .keyboardShortcut("n", modifiers: .command)
                Button("Open…") { appState.open() }
                    .keyboardShortcut("o", modifiers: .command)
                Divider()
                Menu("Load Example") {
                    Section("CIL") {
                        ForEach(Example.allCases) { example in
                            Button(example.rawValue) { appState.loadExample(example) }
                        }
                    }
                    Section("cacalang") {
                        ForEach(CacalangExample.allCases) { example in
                            Button(example.rawValue) { appState.loadCacalangExample(example) }
                        }
                    }
                }
                Divider()
                Button("Save") { appState.save() }
                    .keyboardShortcut("s", modifiers: .command)
                Button("Save As…") { appState.saveAs() }
                    .keyboardShortcut("s", modifiers: [.command, .shift])
            }
            CommandMenu("Build") {
                Button("Compile") { appState.compile() }
                    .keyboardShortcut("b", modifiers: .command)
                Button("Compile & Run") { appState.compileAndRun() }
                    .keyboardShortcut("r", modifiers: .command)
                    .disabled(appState.isRunning)
                Button("Debug…") { appState.openDebugger(openWindow: openWindow) }
                    .keyboardShortcut("d", modifiers: .command)
                    .disabled(appState.isRunning)
                Divider()
                Button("Decompile .ilc…") { appState.openAndDecompile() }
                    .keyboardShortcut("o", modifiers: [.command, .shift])
            }
        }

        Window("Caca Studio – Debugger", id: "debugger") {
            DebugWindowView()
                .environmentObject(appState)
        }
        .commandsRemoved()

        Window("Caca Studio – Console", id: "console") {
            ConsoleWindowView()
                .environmentObject(appState)
        }
        .commandsRemoved()
    }
}
