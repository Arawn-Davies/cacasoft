using System;
using System.Text.RegularExpressions;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace AIL_Studio_Avalonia
{
    /// <summary>
    /// AIL source syntax highlighter, ported from the WinForms IDE's regex-based scheme
    /// (see AIL-Studio/MainForm.cs). AvaloniaEdit re-colorizes per visual line on render,
    /// so unlike the RichTextBox version this needs no debounce timer or redraw-suspend
    /// hack — it's just a per-line transform.
    ///
    /// Comment ("//" or ";" to end of line) always wins: only the text before the first
    /// comment marker is tokenized for mnemonics/registers/numbers/labels.
    /// </summary>
    internal sealed class AilSyntaxColorizer : DocumentColorizingTransformer
    {
        private static readonly string[] Mnemonics =
        {
            "MOV", "MOM", "MOE", "SWP", "TEQ", "TNE", "TLT", "TMT",
            "ADD", "SUB", "INC", "DEC", "MUL", "DIV",
            "SHL", "SHR", "ROL", "ROR", "AND", "BOR", "XOR", "NOT",
            "JMP", "CLL", "RET", "JMT", "JMF", "CLT", "CLF",
            "PSH", "POP", "INB", "INW", "IND", "OUB", "OUW", "OUD",
            "SWI", "KEI"
        };

        private static readonly Regex RxComment = new(@"(//|;).*$", RegexOptions.Compiled);
        private static readonly Regex RxMnemonic = new(
            @"(?<!\w)(" + string.Join("|", Mnemonics) + @")(?!\w)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RxRegister = new(
            @"(?<!\w)(PC|IP|SP|SS|AH|AL|A|BH|BL|B|CH|CL|C|X|Y)(?!\w)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RxHex = new(@"\b0x[0-9A-Fa-f]+\b", RegexOptions.Compiled);
        private static readonly Regex RxCharLit = new(@"'(\\.|[^\\'])'", RegexOptions.Compiled);
        private static readonly Regex RxDecimal = new(@"(?<!\w)\d+(?!\w)", RegexOptions.Compiled);
        private static readonly Regex RxLabel = new(@"^\s*\w+\s*:", RegexOptions.Compiled);

        private static readonly IBrush BComment = new SolidColorBrush(Color.Parse("#6A9955"));
        private static readonly IBrush BMnemonic = new SolidColorBrush(Color.Parse("#569CD6"));
        private static readonly IBrush BRegister = new SolidColorBrush(Color.Parse("#9CDCFE"));
        private static readonly IBrush BNumber = new SolidColorBrush(Color.Parse("#CE9178"));
        private static readonly IBrush BLabel = new SolidColorBrush(Color.Parse("#DCDCAA"));

        protected override void ColorizeLine(DocumentLine line)
        {
            string text = CurrentContext.Document.GetText(line);
            int lineStart = line.Offset;

            var commentMatch = RxComment.Match(text);
            int codeEnd = commentMatch.Success ? commentMatch.Index : text.Length;
            string code = text.Substring(0, codeEnd);

            // Label: colon-terminated identifier at the start of the line.
            var labelMatch = RxLabel.Match(code);
            if (labelMatch.Success)
                Paint(lineStart, labelMatch.Index, labelMatch.Length, BLabel);

            PaintAll(code, lineStart, RxCharLit, BNumber);
            PaintAll(code, lineStart, RxDecimal, BNumber);
            PaintAll(code, lineStart, RxHex, BNumber);
            PaintAll(code, lineStart, RxRegister, BRegister);
            PaintAll(code, lineStart, RxMnemonic, BMnemonic);

            if (commentMatch.Success)
                Paint(lineStart, commentMatch.Index, commentMatch.Length, BComment);
        }

        private void PaintAll(string text, int lineStart, Regex rx, IBrush brush)
        {
            foreach (Match m in rx.Matches(text))
                Paint(lineStart, m.Index, m.Length, brush);
        }

        private void Paint(int lineStart, int index, int length, IBrush brush)
        {
            if (length == 0) return;
            int start = lineStart + index;
            ChangeLinePart(start, start + length,
                element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
