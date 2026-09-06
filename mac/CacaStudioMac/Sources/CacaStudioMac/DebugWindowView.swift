import SwiftUI
import CacaVMKit

/// The step-through debugger window — a SwiftUI port of Caca.VM.Studio's
/// DebugForm: registers + stack on the left, a disassembled memory view with
/// the current instruction highlighted and VM output on the right, a
/// step/run/pause/stop/reset toolbar, and a status bar showing the current
/// decoded instruction.
struct DebugWindowView: View {
    @EnvironmentObject var appState: AppState

    var body: some View {
        Group {
            if let session = appState.debugSession {
                DebugSessionView(session: session)
            } else {
                Text("No active debug session").foregroundStyle(.secondary)
            }
        }
        .frame(minWidth: 900, minHeight: 600)
    }
}

private struct DebugSessionView: View {
    @ObservedObject var session: DebugSession

    var body: some View {
        VStack(spacing: 0) {
            toolbar
            Divider()
            HStack(spacing: 0) {
                registersAndStack
                    .frame(width: 240)
                Divider()
                VStack(spacing: 0) {
                    memoryView
                    Divider()
                    outputView
                        .frame(height: 150)
                }
            }
            Divider()
            statusBar
        }
    }

    // MARK: Toolbar

    private var toolbar: some View {
        HStack(spacing: 10) {
            Button("Step") { session.step() }
                .disabled(session.isHalted || session.isRunning)
                .keyboardShortcut(.init("t"), modifiers: .command)
            Button(session.isRunning ? "Pause" : "Run") {
                session.isRunning ? session.pause() : session.run()
            }
            .disabled(session.isHalted)
            Button("Reset") { session.stopAndReset() }

            Text("Speed:").foregroundStyle(.secondary).font(.caption)
            Slider(value: $session.speed, in: 1...5, step: 1).frame(width: 110)

            Spacer()
            Text("Steps: \(session.steps)").font(.caption).foregroundStyle(.secondary)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
    }

    // MARK: Registers + stack

    private var registersAndStack: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 2) {
                sectionHeader("REGISTERS")
                ForEach(Array(session.registerRows.enumerated()), id: \.offset) { _, row in
                    HStack {
                        ForEach(row) { reg in
                            HStack(spacing: 4) {
                                Text(reg.name).foregroundStyle(Color(red: 0x9C / 255, green: 0xDC / 255, blue: 0xFE / 255))
                                Text(reg.value)
                                    .foregroundStyle(reg.changed
                                        ? Color(red: 0xF4 / 255, green: 0xC8 / 255, blue: 0x42 / 255)
                                        : Color(red: 0xCE / 255, green: 0x91 / 255, blue: 0x78 / 255))
                            }
                            .frame(maxWidth: .infinity, alignment: .leading)
                        }
                    }
                }
                sectionHeader("STACK").padding(.top, 8)
                if session.stackBytes.isEmpty {
                    Text("(empty)").foregroundStyle(.secondary)
                } else {
                    ForEach(session.stackBytes) { b in
                        HStack {
                            Text("[\(String(format: "%X", b.address))]").foregroundStyle(Color(red: 0x9C / 255, green: 0xDC / 255, blue: 0xFE / 255))
                            Text("0x" + String(format: "%02X", b.value)).foregroundStyle(Color(red: 0xCE / 255, green: 0x91 / 255, blue: 0x78 / 255))
                        }
                    }
                }
            }
            .font(.system(size: 11, design: .monospaced))
            .padding(8)
        }
        .background(Color(red: 0x25 / 255, green: 0x25 / 255, blue: 0x26 / 255))
    }

    private func sectionHeader(_ text: String) -> some View {
        Text(text).font(.caption).bold().foregroundStyle(.secondary)
    }

    // MARK: Memory / instruction view

    private var memoryView: some View {
        ScrollViewReader { proxy in
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 0) {
                    Text("  ADDR   00  01  02  03  04  05   INSTRUCTION")
                        .foregroundStyle(.secondary)
                    ForEach(session.memoryRows) { row in
                        HStack(spacing: 8) {
                            Text("0x" + String(format: "%04X", row.address))
                                .foregroundStyle(row.isCurrent ? .white : Color(red: 0x85 / 255, green: 0x85 / 255, blue: 0x85 / 255))
                            Text(row.bytes.map { String(format: "%02X", $0) }.joined(separator: " "))
                                .foregroundStyle(row.isCurrent ? .white : Color(red: 0xD4 / 255, green: 0xD4 / 255, blue: 0xD4 / 255))
                            Text(row.decoded)
                                .foregroundStyle(row.isCurrent ? .white : Color(red: 0xD4 / 255, green: 0xD4 / 255, blue: 0xD4 / 255))
                            Spacer()
                        }
                        .padding(.horizontal, 4)
                        .background(row.isCurrent ? Color(red: 0x26 / 255, green: 0x4F / 255, blue: 0x78 / 255) : .clear)
                        .id(row.id)
                    }
                }
                .font(.system(size: 11, design: .monospaced))
                .padding(4)
            }
            .onChange(of: session.steps) { _ in
                if let current = session.memoryRows.first(where: { $0.isCurrent }) {
                    withAnimation { proxy.scrollTo(current.id, anchor: .center) }
                }
            }
        }
        .background(Color(red: 0x1E / 255, green: 0x1E / 255, blue: 0x1E / 255))
    }

    // MARK: Output

    private var outputView: some View {
        VStack(alignment: .leading, spacing: 0) {
            Text("VM OUTPUT").font(.caption).foregroundStyle(.secondary).padding(4)
            OutputConsoleView(segments: session.output)
        }
    }

    // MARK: Status bar

    private var statusBar: some View {
        HStack {
            Text(session.isHalted ? "■ Halted" : (session.isRunning ? "● Running" : "⏸ Paused"))
                .foregroundStyle(session.isHalted ? .red : .green)
            Divider().frame(height: 12)
            Text(session.currentInstructionText).foregroundStyle(.secondary)
            Spacer()
        }
        .font(.caption)
        .padding(.horizontal, 10)
        .padding(.vertical, 4)
    }
}
