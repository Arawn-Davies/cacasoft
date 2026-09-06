using System;
using System.Drawing;
using System.Windows.Forms;

namespace Caca.VM.Studio
{
    /// <summary>
    /// Launch splash with a loading bar, shown before <see cref="MainForm"/> —
    /// the WinForms counterpart to CacaStudioMac's SplashView. Steps are
    /// cosmetic (there's no expensive engine startup to hide) but real: each
    /// one names an actual part of Caca Studio's own startup path, run in
    /// order, matching the Swift IDE's splash 1:1 for both text and colours.
    /// </summary>
    public sealed class SplashForm : Form
    {
        private static readonly Color CBackground = Color.FromArgb(0x1E, 0x1E, 0x1E);
        private static readonly Color CDefault    = Color.FromArgb(0xD4, 0xD4, 0xD4);
        private static readonly Color CSubtitle   = Color.FromArgb(0xAA, 0xAA, 0xAA);
        private static readonly Color CStatus     = Color.FromArgb(0x85, 0x85, 0x85);

        private readonly ProgressBar _bar;
        private readonly Label _status;
        private readonly System.Windows.Forms.Timer _timer;
        private int _stepIndex;

        private static readonly (string Text, int Value)[] Steps =
        {
            ("Loading CIL & cacalang language definitions…", 25),
            ("Initializing CacaVM…",                          55),
            ("Preparing editor and syntax highlighting…",     80),
            ("Ready.",                                        100),
        };

        /// <summary>Set once all steps have run — <see cref="Program.Main"/> polls this to know when to close the splash.</summary>
        public bool IsFinished { get; private set; }

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition   = FormStartPosition.CenterScreen;
            Size            = new Size(480, 320);
            BackColor       = CBackground;
            ForeColor       = CDefault;
            ShowInTaskbar   = false;
            TopMost         = true;

            var title = new Label
            {
                Text      = "Caca Studio",
                Font      = new Font("Segoe UI", 24f, FontStyle.Bold),
                ForeColor = CDefault,
                Dock      = DockStyle.Top,
                Height    = 160,
                TextAlign = ContentAlignment.BottomCenter,
            };
            var subtitle = new Label
            {
                Text      = "IDE for Caca Intermediate Language & cacalang",
                Font      = new Font("Segoe UI", 10f),
                ForeColor = CSubtitle,
                Dock      = DockStyle.Top,
                Height    = 40,
                TextAlign = ContentAlignment.TopCenter,
            };
            _status = new Label
            {
                Text      = "Starting…",
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = CStatus,
                Dock      = DockStyle.Bottom,
                Height    = 28,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            _bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = 100,
                Value   = 0,
                Dock    = DockStyle.Bottom,
                Height  = 8,
            };

            Controls.Add(_status);
            Controls.Add(_bar);
            Controls.Add(subtitle);
            Controls.Add(title);

            _timer = new System.Windows.Forms.Timer { Interval = 220 };
            _timer.Tick += (_, _) => Advance();
            _timer.Start();
        }

        private void Advance()
        {
            if (_stepIndex >= Steps.Length)
            {
                _timer.Stop();
                IsFinished = true;
                return;
            }

            var (text, value) = Steps[_stepIndex++];
            _status.Text = text;
            _bar.Value   = value;
        }
    }
}
