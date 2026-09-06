import AppKit
import SwiftUI

/// A line-numbered, syntax-highlighted CIL editor — an AppKit-backed
/// NSViewRepresentable standing in for MainForm.cs's CodeEditor +
/// LineNumberPanel pairing.
///
/// Wraps everything in EditorContainerView (a plain NSView) rather than
/// handing SwiftUI a bare NSScrollView directly, with explicit
/// NSLayoutConstraints pinning both the scroll view and the line-number
/// gutter to it — the structure a known-working NSTextView-in-SwiftUI
/// wrapper uses.
///
/// Line numbers are a plain sibling NSView (LineNumberGutterView), NOT an
/// NSRulerView registered via scrollView.verticalRulerView. That was tried
/// first and is the textbook-standard approach, but on this exact macOS
/// build (26.6.2) it reproducibly breaks the scroll view's document view
/// entirely — the text view stopped painting any glyphs at all the moment a
/// custom NSRulerView was attached via hasVerticalRuler/verticalRulerView/
/// rulersVisible, while the ruler itself (and the layout manager it queries)
/// kept working perfectly, which is what made this so misleading: line
/// numbers rendering correctly looked like proof the text system was fine.
/// Confirmed by bisection: same NSScrollView+NSTextView setup, only the
/// ruler removed, and text rendered immediately. Not an isRichText, font-
/// attribute, autoresizing-mask, layer-backing, or dark-mode issue — every
/// one of those was independently tried and ruled out first.
struct CodeEditorView: NSViewRepresentable {
    @Binding var text: String
    var onCursorMove: (Int, Int) -> Void = { _, _ in }
    var onEdit: () -> Void = {}

    func makeNSView(context: Context) -> EditorContainerView {
        let container = EditorContainerView()
        let textView = container.textView

        textView.delegate = context.coordinator
        textView.isEditable = true
        // NOT isRichText = false: that forces NSTextView's plain-text
        // normalization, which actively strips/resets the syntax
        // highlighter's per-character foreground-color runs shortly after
        // they're applied.
        textView.allowsUndo = true
        textView.font = SyntaxHighlighting.editorFont
        textView.backgroundColor = SyntaxHighlighting.background
        textView.insertionPointColor = SyntaxHighlighting.defaultColor
        textView.textContainerInset = NSSize(width: 4, height: 4)
        textView.string = text
        textView.textStorage?.delegate = context.coordinator

        context.coordinator.textView = textView
        context.coordinator.container = container
        context.coordinator.lastKnownText = text
        SyntaxHighlighting.apply(to: textView.textStorage!)

        NotificationCenter.default.addObserver(
            context.coordinator, selector: #selector(Coordinator.selectionDidChange),
            name: NSTextView.didChangeSelectionNotification, object: textView)

        return container
    }

    func updateNSView(_ container: EditorContainerView, context: Context) {
        guard let textView = context.coordinator.textView else { return }
        // Only push text down when it changed from OUTSIDE the editor (Open,
        // Load Example, Decompile) — otherwise every keystroke would fight the
        // NSTextView's own undo stack and cursor position.
        if text != context.coordinator.lastKnownText {
            textView.string = text
            context.coordinator.lastKnownText = text
            SyntaxHighlighting.apply(to: textView.textStorage!)
            container.gutterView.needsDisplay = true
        }
    }

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    final class Coordinator: NSObject, NSTextViewDelegate, NSTextStorageDelegate {
        var parent: CodeEditorView
        weak var textView: NSTextView?
        weak var container: EditorContainerView?
        var lastKnownText = ""
        private var isHighlighting = false

        init(_ parent: CodeEditorView) { self.parent = parent }

        /// Only fires for genuine user edits — NSTextView does NOT call this
        /// for a programmatic `.string =` assignment (that's the whole point
        /// of using it, rather than a view-level `.onChange(of: text)`,
        /// to mark the document modified: New/Open/Load Example/Decompile
        /// all replace `text` wholesale too, and a plain `onChange` can't
        /// tell "the user typed" apart from "the app just loaded a
        /// document" — it fired for both, clobbering `modified = false`
        /// right back to `true` after every load.
        func textDidChange(_ notification: Notification) {
            guard let textView = notification.object as? NSTextView else { return }
            lastKnownText = textView.string
            parent.text = textView.string
            parent.onEdit()
            container?.gutterView.needsDisplay = true
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
            container?.gutterView.needsDisplay = true
        }
    }
}

/// Owns the line-number gutter + scroll view + non-wrapping text view, and
/// pins both to itself with explicit NSLayoutConstraints — the structure a
/// known-working NSTextView-in-SwiftUI wrapper uses, rather than handing
/// SwiftUI a bare, unconstrained NSScrollView directly.
final class EditorContainerView: NSView {
    let textView: NSTextView
    let gutterView: LineNumberGutterView
    private let scrollView: NSScrollView
    private var constraintsInstalled = false
    private var boundsObserver: NSObjectProtocol?

    override init(frame frameRect: NSRect) {
        let textView = NSTextView.nonWrapping()
        let scrollView = NSScrollView()
        let gutterView = LineNumberGutterView(textView: textView)
        self.textView = textView
        self.scrollView = scrollView
        self.gutterView = gutterView

        super.init(frame: frameRect)

        scrollView.hasVerticalScroller = true
        scrollView.hasHorizontalScroller = true
        scrollView.autohidesScrollers = false
        scrollView.drawsBackground = true
        scrollView.documentView = textView
        scrollView.translatesAutoresizingMaskIntoConstraints = false
        scrollView.contentView.postsBoundsChangedNotifications = true

        gutterView.translatesAutoresizingMaskIntoConstraints = false

        addSubview(gutterView)
        addSubview(scrollView)

        boundsObserver = NotificationCenter.default.addObserver(
            forName: NSView.boundsDidChangeNotification, object: scrollView.contentView,
            queue: .main) { [weak gutterView] _ in gutterView?.needsDisplay = true }
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    deinit {
        if let boundsObserver { NotificationCenter.default.removeObserver(boundsObserver) }
    }

    override func viewWillDraw() {
        super.viewWillDraw()
        guard !constraintsInstalled else { return }
        constraintsInstalled = true

        NSLayoutConstraint.activate([
            gutterView.topAnchor.constraint(equalTo: topAnchor),
            gutterView.leadingAnchor.constraint(equalTo: leadingAnchor),
            gutterView.bottomAnchor.constraint(equalTo: bottomAnchor),
            gutterView.widthAnchor.constraint(equalToConstant: LineNumberGutterView.width),

            scrollView.topAnchor.constraint(equalTo: topAnchor),
            scrollView.leadingAnchor.constraint(equalTo: gutterView.trailingAnchor),
            scrollView.trailingAnchor.constraint(equalTo: trailingAnchor),
            scrollView.bottomAnchor.constraint(equalTo: bottomAnchor),
        ])
    }
}

extension NSTextView {
    /// Disables word wrap (RichTextBox's WordWrap=false in the original) so
    /// every paragraph is exactly one line fragment — required for the
    /// line-number gutter to line up 1:1 with source lines. A plain factory
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
        // NOT [.width, .height]: that tells AppKit "stretch to match my
        // superview's size", directly fighting isHorizontallyResizable/
        // isVerticallyResizable's own "grow to match my content" resizing.
        view.autoresizingMask = []
        view.minSize = NSSize(width: 0, height: 0)
        view.maxSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        view.textContainer?.widthTracksTextView = false
        view.textContainer?.containerSize = NSSize(width: CGFloat.greatestFiniteMagnitude, height: CGFloat.greatestFiniteMagnitude)
        return view
    }
}

/// Draws 1-based line numbers alongside the editor, matching MainForm.cs's
/// LineNumberPanel (current-line highlight included) — a plain NSView, not
/// an NSRulerView (see CodeEditorView's doc comment for why).
final class LineNumberGutterView: NSView {
    static let width: CGFloat = 44
    private weak var textView: NSTextView?

    init(textView: NSTextView) {
        self.textView = textView
        super.init(frame: .zero)
    }

    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override var isFlipped: Bool { true } // matches NSTextView's own flipped coordinate space

    override func draw(_ dirtyRect: NSRect) {
        SyntaxHighlighting.gutterBg.setFill()
        bounds.fill()

        guard let textView, let layoutManager = textView.layoutManager, let container = textView.textContainer,
              let scrollView = textView.enclosingScrollView else { return }

        let visibleRect = scrollView.contentView.bounds
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

        let font = textView.font ?? SyntaxHighlighting.editorFont
        let inset = textView.textContainerInset

        layoutManager.enumerateLineFragments(forGlyphRange: glyphRange) { lineRect, _, _, glyphRangeForLine, _ in
            let isCurrent = lineNumber == curLineNumber
            let y = lineRect.minY - visibleRect.minY + inset.height

            if isCurrent {
                NSColor(red: 0x2A / 255, green: 0x2D / 255, blue: 0x31 / 255, alpha: 1)
                    .setFill()
                NSRect(x: 0, y: y, width: Self.width, height: lineRect.height).fill()
            }

            let numberString = "\(lineNumber)" as NSString
            let color = isCurrent
                ? NSColor(red: 0xC6 / 255, green: 0xC6 / 255, blue: 0xC6 / 255, alpha: 1)
                : SyntaxHighlighting.lineNumberFg
            let attrs: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: color]
            let size = numberString.size(withAttributes: attrs)
            numberString.draw(at: NSPoint(x: Self.width - size.width - 6, y: y), withAttributes: attrs)

            lineNumber += 1
        }
    }
}
