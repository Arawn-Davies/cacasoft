import AppKit
import SwiftUI

/// A line-numbered, syntax-highlighted CIL editor — an AppKit-backed
/// NSViewRepresentable standing in for MainForm.cs's CodeEditor +
/// LineNumberPanel pairing (a RichTextBox can't be ported directly; NSTextView
/// + a custom NSRulerView is the native equivalent).
struct CodeEditorView: NSViewRepresentable {
    @Binding var text: String
    var onCursorMove: (Int, Int) -> Void = { _, _ in }

    func makeNSView(context: Context) -> NSScrollView {
        let textView = NSTextView.nonWrapping()
        textView.delegate = context.coordinator
        textView.isEditable = true
        textView.isRichText = false
        textView.allowsUndo = true
        textView.font = Self.monoFont(10)
        textView.backgroundColor = SyntaxHighlighting.background
        textView.insertionPointColor = SyntaxHighlighting.defaultColor
        textView.textContainerInset = NSSize(width: 4, height: 4)
        textView.string = text
        textView.textStorage?.delegate = context.coordinator

        let scrollView = NSScrollView()
        scrollView.hasVerticalScroller = true
        scrollView.hasHorizontalScroller = true
        scrollView.documentView = textView
        scrollView.hasVerticalRuler = true
        let ruler = LineNumberRulerView(textView: textView)
        scrollView.verticalRulerView = ruler
        scrollView.rulersVisible = true

        context.coordinator.textView = textView
        context.coordinator.lastKnownText = text
        SyntaxHighlighting.apply(to: textView.textStorage!)

        NotificationCenter.default.addObserver(
            context.coordinator, selector: #selector(Coordinator.selectionDidChange),
            name: NSTextView.didChangeSelectionNotification, object: textView)

        return scrollView
    }

    func updateNSView(_ scrollView: NSScrollView, context: Context) {
        guard let textView = context.coordinator.textView else { return }
        // Only push text down when it changed from OUTSIDE the editor (Open,
        // Load Example, Decompile) — otherwise every keystroke would fight the
        // NSTextView's own undo stack and cursor position.
        if text != context.coordinator.lastKnownText {
            textView.string = text
            context.coordinator.lastKnownText = text
            SyntaxHighlighting.apply(to: textView.textStorage!)
        }
    }

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    static func monoFont(_ size: CGFloat) -> NSFont {
        NSFont.monospacedSystemFont(ofSize: size, weight: .regular)
    }

    final class Coordinator: NSObject, NSTextViewDelegate, NSTextStorageDelegate {
        var parent: CodeEditorView
        weak var textView: NSTextView?
        var lastKnownText = ""
        private var isHighlighting = false

        init(_ parent: CodeEditorView) { self.parent = parent }

        func textDidChange(_ notification: Notification) {
            guard let textView = notification.object as? NSTextView else { return }
            lastKnownText = textView.string
            parent.text = textView.string
        }

        /// Auto-indent: preserve the current line's leading whitespace on
        /// Enter — a port of MainForm.cs's OnEditorKeyDown.
        func textView(_ textView: NSTextView, doCommandBy commandSelector: Selector) -> Bool {
            guard commandSelector == #selector(NSResponder.insertNewline(_:)) else { return false }

            let content = textView.string as NSString
            let lineRange = content.lineRange(for: NSRange(location: textView.selectedRange().location, length: 0))
            let currentLine = content.substring(with: lineRange)

            var indent = ""
            for character in currentLine {
                if character == " " || character == "\t" { indent.append(character) } else { break }
            }

            textView.insertText("\n" + indent, replacementRange: textView.selectedRange())
            return true
        }

        func textStorage(_ textStorage: NSTextStorage, didProcessEditing editedMask: NSTextStorageEditActions, range editedRange: NSRange, changeInLength delta: Int) {
            guard !isHighlighting, editedMask.contains(.editedCharacters) else { return }
            isHighlighting = true
            SyntaxHighlighting.apply(to: textStorage)
            isHighlighting = false
        }

        @objc func selectionDidChange() {
            guard let textView else { return }
            let text = textView.string as NSString
            let location = textView.selectedRange().location
            var line = 1
            var lastNewline = -1
            text.enumerateSubstrings(in: NSRange(location: 0, length: min(location, text.length)), options: .byLines) { _, range, _, _ in
                line += 1
                lastNewline = range.location + range.length
            }
            let column = location - max(lastNewline, 0) + 1
            parent.onCursorMove(line, column)
        }
    }
}

extension NSTextView {
    /// Disables word wrap (RichTextBox's WordWrap=false in the original) so
    /// every paragraph is exactly one line fragment — required for the
    /// line-number ruler to line up 1:1 with source lines. A plain factory
    /// function rather than a subclass: NSTextView's designated initializer
    /// is `init(frame:textContainer:)`, and overriding it in a subclass hides
    /// the `init(frame:)` convenience initializer that builds a real
    /// text storage/layout manager/container stack — passing `textContainer:
    /// nil` explicitly (to keep just one initializer) instead builds a text
    /// view with no text system at all, crashing the first time anything
    /// touches `.textStorage`.
    static func nonWrapping() -> NSTextView {
        let view = NSTextView(frame: .zero)
        view.isHorizontallyResizable = true
        view.isVerticallyResizable = true
        view.autoresizingMask = [.width, .height]
        view.textContainer?.widthTracksTextView = false
        view.textContainer?.containerSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        return view
    }
}

/// Draws 1-based line numbers in the scroll view's gutter, matching
/// MainForm.cs's LineNumberPanel (current-line highlight included).
final class LineNumberRulerView: NSRulerView {
    private weak var textView: NSTextView?

    init(textView: NSTextView) {
        self.textView = textView
        super.init(scrollView: textView.enclosingScrollView, orientation: .verticalRuler)
        clientView = textView
        ruleThickness = 44
    }

    required init(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func drawHashMarksAndLabels(in rect: NSRect) {
        guard let textView, let layoutManager = textView.layoutManager, let container = textView.textContainer else { return }

        SyntaxHighlighting.gutterBg.setFill()
        rect.fill()

        let visibleRect = scrollView?.contentView.bounds ?? .zero
        let content = textView.string as NSString
        let glyphRange = layoutManager.glyphRange(forBoundingRect: visibleRect, in: container)
        let firstChar = layoutManager.characterIndexForGlyph(at: glyphRange.location)

        var lineNumber = 1
        content.enumerateSubstrings(in: NSRange(location: 0, length: firstChar), options: .byLines) { _, _, _, _ in
            lineNumber += 1
        }

        let curLine = textView.selectedRange().location
        var curLineNumber = 1
        content.enumerateSubstrings(in: NSRange(location: 0, length: min(curLine, content.length)), options: .byLines) { _, _, _, _ in
            curLineNumber += 1
        }

        let font = textView.font ?? CodeEditorView.monoFont(10)
        let inset = textView.textContainerInset

        layoutManager.enumerateLineFragments(forGlyphRange: glyphRange) { lineRect, _, _, glyphRangeForLine, _ in
            let isCurrent = lineNumber == curLineNumber
            let y = lineRect.minY - visibleRect.minY + inset.height

            if isCurrent {
                NSColor(red: 0x2A / 255, green: 0x2D / 255, blue: 0x31 / 255, alpha: 1)
                    .setFill()
                NSRect(x: 0, y: y, width: self.ruleThickness, height: lineRect.height).fill()
            }

            let numberString = "\(lineNumber)" as NSString
            let color = isCurrent
                ? NSColor(red: 0xC6 / 255, green: 0xC6 / 255, blue: 0xC6 / 255, alpha: 1)
                : SyntaxHighlighting.lineNumberFg
            let attrs: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: color]
            let size = numberString.size(withAttributes: attrs)
            numberString.draw(at: NSPoint(x: self.ruleThickness - size.width - 6, y: y), withAttributes: attrs)

            lineNumber += 1
        }
    }
}
