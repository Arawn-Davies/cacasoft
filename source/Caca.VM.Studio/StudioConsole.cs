using System;
using System.Drawing;
using System.Windows.Forms;
using Caca.VM.Handlers;

namespace Caca.VM.Studio
{
    /// <summary>
    /// Bridges Caca.VM I/O to the IDE output panel.
    /// Writes are dispatched to the UI thread safely.
    /// </summary>
    internal sealed class StudioConsole : VConsole
    {
        private readonly RichTextBox _output;

        public StudioConsole(RichTextBox output)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        public override void Write(string text) =>
            Append(text, Color.Empty);

        public override void Write(char ch) =>
            Append(ch.ToString(), Color.Empty);

        public override void WriteLine(string text) =>
            Append(text + Environment.NewLine, Color.Empty);

        public override byte Read()
        {
            string result = string.Empty;
            _output.Invoke((Action)(() =>
                result = ShowInputDialog("VM is waiting for a single character:", "Caca Studio – Input")));
            return result.Length > 0 ? (byte)result[0] : (byte)0;
        }

        public override string ReadLine()
        {
            string result = string.Empty;
            _output.Invoke((Action)(() =>
                result = ShowInputDialog("VM is waiting for input:", "Caca Studio – Input")));
            return result;
        }

        private static string ShowInputDialog(string prompt, string title)
        {
            using var dlg = new Form
            {
                Text = title,
                Width = 380,
                Height = 140,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false, MinimizeBox = false,
                BackColor = Color.FromArgb(0x25, 0x25, 0x26),
                ForeColor = Color.FromArgb(0xCC, 0xCC, 0xCC),
            };
            var lbl = new Label { Left = 10, Top = 10, Width = 340, Text = prompt,
                ForeColor = Color.FromArgb(0xCC, 0xCC, 0xCC), BackColor = Color.Transparent };
            var txt = new TextBox { Left = 10, Top = 34, Width = 340,
                BackColor = Color.FromArgb(0x1E, 0x1E, 0x1E),
                ForeColor = Color.FromArgb(0xD4, 0xD4, 0xD4),
                BorderStyle = BorderStyle.FixedSingle };
            var btn = new Button { Left = 275, Top = 64, Width = 75, Text = "OK",
                DialogResult = DialogResult.OK,
                BackColor = Color.FromArgb(0x0E, 0x63, 0x9D),
                ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btn.FlatAppearance.BorderSize = 0;
            dlg.Controls.AddRange(new Control[] { lbl, txt, btn });
            dlg.AcceptButton = btn;
            return dlg.ShowDialog() == DialogResult.OK ? txt.Text : string.Empty;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void Append(string text, Color color)
        {
            if (_output.IsDisposed) return;

            void DoAppend()
            {
                if (_output.IsDisposed) return;
                int start = _output.TextLength;
                _output.AppendText(text);
                if (color != Color.Empty)
                {
                    _output.Select(start, text.Length);
                    _output.SelectionColor = color;
                    _output.SelectionLength = 0;
                }
                _output.ScrollToCaret();
            }

            if (_output.InvokeRequired)
                _output.Invoke((Action)DoAppend);
            else
                DoAppend();
        }
    }
}
