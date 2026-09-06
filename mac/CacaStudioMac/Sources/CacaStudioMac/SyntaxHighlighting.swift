import AppKit

/// CIL syntax colouring — a Swift/NSRegularExpression port of MainForm.cs's
/// ApplySyntaxHighlighting (same mnemonic list, same colours, same
/// precedence: comments painted last so they win over everything else).
enum SyntaxHighlighting {
    static let background = NSColor(red: 0x1E / 255, green: 0x1E / 255, blue: 0x1E / 255, alpha: 1)
    static let defaultColor = NSColor(red: 0xD4 / 255, green: 0xD4 / 255, blue: 0xD4 / 255, alpha: 1)
    static let comment = NSColor(red: 0x6A / 255, green: 0x99 / 255, blue: 0x55 / 255, alpha: 1)
    static let mnemonic = NSColor(red: 0x56 / 255, green: 0x9C / 255, blue: 0xD6 / 255, alpha: 1)
    static let register = NSColor(red: 0x9C / 255, green: 0xDC / 255, blue: 0xFE / 255, alpha: 1)
    static let number = NSColor(red: 0xCE / 255, green: 0x91 / 255, blue: 0x78 / 255, alpha: 1)
    static let label = NSColor(red: 0xDC / 255, green: 0xDC / 255, blue: 0xAA / 255, alpha: 1)
    static let lineNumberFg = NSColor(red: 0x85 / 255, green: 0x85 / 255, blue: 0x85 / 255, alpha: 1)
    static let gutterBg = NSColor(red: 0x25 / 255, green: 0x25 / 255, blue: 0x26 / 255, alpha: 1)

    private static let mnemonics = [
        "MOV", "MOM", "MOE", "SWP", "TEQ", "TNE", "TLT", "TMT",
        "ADD", "SUB", "INC", "DEC", "MUL", "DIV",
        "SHL", "SHR", "ROL", "ROR", "AND", "BOR", "XOR", "NOT",
        "JMP", "CLL", "RET", "JMT", "JMF", "CLT", "CLF",
        "PSH", "POP",
        "INB", "INW", "IND", "OUB", "OUW", "OUD",
        "SWI", "KEI",
    ]

    private static let rxComment = try! NSRegularExpression(pattern: #"(//|;).*$"#, options: [.anchorsMatchLines])
    private static let rxLabel = try! NSRegularExpression(pattern: #"^\s*\w+\s*:"#, options: [.anchorsMatchLines])
    private static let rxMnemonic = try! NSRegularExpression(
        pattern: #"(?<!\w)(" + mnemonics.joined(separator: "|") + #")(?!\w)"#, options: [.caseInsensitive])
    private static let rxRegister = try! NSRegularExpression(
        pattern: #"(?<!\w)(PC|IP|SP|SS|AH|AL|A|BH|BL|B|CH|CL|C|X|Y)(?!\w)"#, options: [.caseInsensitive])
    private static let rxHex = try! NSRegularExpression(pattern: #"\b0x[0-9A-Fa-f]+\b"#)
    private static let rxDecimal = try! NSRegularExpression(pattern: #"(?<!\w)\d+(?!\w)"#)
    private static let rxCharLit = try! NSRegularExpression(pattern: #"'(\\.|[^\\'])'"#)

    static let editorFont = NSFont.monospacedSystemFont(ofSize: 10, weight: .regular)

    /// Re-colours the whole string in place. CIL programs are tiny (a few
    /// hundred lines at most), so a full re-highlight on every keystroke is
    /// simpler and just as fast as the C# editor's 250ms-debounced version.
    ///
    /// The baseline pass MUST set `.font` alongside `.foregroundColor`:
    /// `NSMutableAttributedString.setAttributes(_:range:)` replaces the
    /// entire attribute dictionary for that range, not just the keys given —
    /// omitting `.font` here silently strips it from the whole document on
    /// every keystroke, and a font-less run renders no glyphs at all.
    static func apply(to storage: NSTextStorage) {
        let text = storage.string as NSString
        guard text.length > 0 else { return }

        storage.beginEditing()
        storage.setAttributes([.foregroundColor: defaultColor, .font: editorFont], range: NSRange(location: 0, length: text.length))

        colour(rxCharLit, text, number, storage)
        colour(rxDecimal, text, number, storage)
        colour(rxHex, text, number, storage)
        colour(rxRegister, text, register, storage)
        colour(rxMnemonic, text, mnemonic, storage)
        colour(rxLabel, text, label, storage)
        colour(rxComment, text, comment, storage) // wins over everything

        storage.endEditing()
    }

    private static func colour(_ rx: NSRegularExpression, _ text: NSString, _ color: NSColor, _ storage: NSTextStorage) {
        let full = NSRange(location: 0, length: text.length)
        rx.enumerateMatches(in: text as String, range: full) { match, _, _ in
            guard let range = match?.range else { return }
            storage.addAttribute(.foregroundColor, value: color, range: range)
        }
    }
}
