import SwiftUI

/// Launch splash with a loading bar, shown before the IDE window's content —
/// the SwiftUI counterpart to Caca.VM.Studio's SplashForm. Steps are cosmetic
/// (there's no expensive engine startup to hide) but real: each one names an
/// actual part of CacaStudioMac's own startup path, run in order.
struct SplashView: View {
    let progress: Double
    let status: String

    var body: some View {
        VStack(spacing: 18) {
            Spacer()
            Text("🗑️")
                .font(.system(size: 56))
            Text("Caca Studio")
                .font(.system(size: 28, weight: .semibold, design: .rounded))
                .foregroundStyle(Color(red: 0xD4 / 255, green: 0xD4 / 255, blue: 0xD4 / 255))
            Text("IDE for Caca Intermediate Language & cacalang")
                .font(.callout)
                .foregroundStyle(Color(red: 0xAA / 255, green: 0xAA / 255, blue: 0xAA / 255))
            Spacer()
            VStack(spacing: 8) {
                ProgressView(value: progress)
                    .progressViewStyle(.linear)
                    .frame(width: 340)
                Text(status)
                    .font(.caption)
                    .foregroundStyle(Color(red: 0x85 / 255, green: 0x85 / 255, blue: 0x85 / 255))
            }
            .padding(.bottom, 36)
        }
        .frame(width: 480, height: 320)
        .background(Color(red: 0x1E / 255, green: 0x1E / 255, blue: 0x1E / 255))
    }
}

/// Runs the splash sequence, then swaps in the real IDE content.
struct RootView: View {
    @EnvironmentObject var appState: AppState
    @State private var isLoading = true
    @State private var progress: Double = 0
    @State private var status = "Starting…"

    private static let steps: [(String, Double)] = [
        ("Loading CIL & cacalang language definitions…", 0.25),
        ("Initializing CacaVMKit…", 0.55),
        ("Preparing editor and syntax highlighting…", 0.8),
        ("Ready.", 1.0),
    ]

    var body: some View {
        Group {
            if isLoading {
                SplashView(progress: progress, status: status)
            } else {
                ContentView()
            }
        }
        .task {
            guard isLoading else { return }
            for (text, value) in Self.steps {
                status = text
                withAnimation(.easeInOut(duration: 0.25)) { progress = value }
                try? await Task.sleep(nanoseconds: 220_000_000)
            }
            withAnimation(.easeInOut(duration: 0.2)) { isLoading = false }
        }
    }
}
