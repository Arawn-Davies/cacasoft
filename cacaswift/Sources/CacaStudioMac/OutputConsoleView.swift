import AppKit
import SwiftUI

/// A read-only, colour-coded output pane — the AppKit equivalent of
/// MainForm.cs's `_output` RichTextBox (and DebugForm.cs's `_outBox`),
/// auto-scrolling to the newest line as segments are appended.
struct OutputConsoleView: NSViewRepresentable {
    var segments: [OutputSegment]
    var font: NSFont = NSFont.monospacedSystemFont(ofSize: 9, weight: .regular)

    func makeNSView(context: Context) -> NSScrollView {
        let textView = NSTextView.nonWrapping()
        textView.isEditable = false
        textView.isSelectable = true
        textView.drawsBackground = true
        textView.backgroundColor = NSColor(red: 0x12 / 255, green: 0x12 / 255, blue: 0x12 / 255, alpha: 1)
        textView.textContainerInset = NSSize(width: 4, height: 4)

        let scrollView = NSScrollView()
        scrollView.hasVerticalScroller = true
        scrollView.hasHorizontalScroller = true
        scrollView.documentView = textView
        context.coordinator.textView = textView
        return scrollView
    }

    func updateNSView(_ scrollView: NSScrollView, context: Context) {
        guard let textView = context.coordinator.textView, segments.count != context.coordinator.renderedCount else { return }

        let attributed = NSMutableAttributedString()
        for segment in segments {
            attributed.append(NSAttributedString(string: segment.text, attributes: [
                .foregroundColor: NSColor(segment.kind.color),
                .font: font,
            ]))
        }
        textView.textStorage?.setAttributedString(attributed)
        context.coordinator.renderedCount = segments.count
        textView.scrollToEndOfDocument(nil)
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    final class Coordinator {
        weak var textView: NSTextView?
        var renderedCount = 0
    }
}
