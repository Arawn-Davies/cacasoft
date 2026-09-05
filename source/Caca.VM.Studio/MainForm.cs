using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Caca.VM;

namespace Caca.VM.Studio
{
    /// <summary>
    /// Caca Studio — dark-themed WinForms IDE for Caca Intermediate Language.
    /// No external NuGet packages; all UI built programmatically.
    /// </summary>
    public sealed class MainForm : Form
    {
        // ── Win32 helpers for flicker-free RichTextBox updates ────────────────
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int SendMessage(IntPtr hWnd, int wMsg, bool wParam, int lParam);
        private const int WM_SETREDRAW = 11;

        // ── Colours ───────────────────────────────────────────────────────────
        private static readonly Color CBackground    = Color.FromArgb(0x1E, 0x1E, 0x1E);
        private static readonly Color CSidebar       = Color.FromArgb(0x25, 0x25, 0x26);
        private static readonly Color CToolbar       = Color.FromArgb(0x2D, 0x2D, 0x30);
        private static readonly Color CDefault       = Color.FromArgb(0xD4, 0xD4, 0xD4);
        private static readonly Color CLineNumFg     = Color.FromArgb(0x85, 0x85, 0x85);
        private static readonly Color CComment       = Color.FromArgb(0x6A, 0x99, 0x55);
        private static readonly Color CMnemonic      = Color.FromArgb(0x56, 0x9C, 0xD6);
        private static readonly Color CRegister      = Color.FromArgb(0x9C, 0xDC, 0xFE);
        private static readonly Color CNumber        = Color.FromArgb(0xCE, 0x91, 0x78);
        private static readonly Color CLabel         = Color.FromArgb(0xDC, 0xDC, 0xAA);
        private static readonly Color COutputInfo    = Color.FromArgb(0x9C, 0xDC, 0xFE);
        private static readonly Color COutputSuccess = Color.FromArgb(0x6A, 0x99, 0x55);
        private static readonly Color COutputError   = Color.FromArgb(0xF4, 0x47, 0x47);

        // ── Controls ─────────────────────────────────────────────────────────
        private MenuStrip    _menu        = null!;
        private ToolStrip    _toolbar     = null!;
        private SplitContainer _split     = null!;
        private LineNumberPanel _lineNums = null!;
        private CodeEditor   _editor      = null!;
        private Panel        _outputPanel = null!;
        private RichTextBox  _output      = null!;
        private Button       _undockBtn   = null!;
        private ToolTip      _undockTip   = null!;
        private StatusStrip  _status      = null!;
        private ToolStripStatusLabel _statusFile  = null!;
        private ToolStripStatusLabel _statusPos   = null!;

        // ── State ─────────────────────────────────────────────────────────────
        private string  _filePath    = string.Empty;
        private bool    _modified    = false;
        private bool    _highlighting = false;
        private byte[]  _lastBuild   = Array.Empty<byte>();
        private System.Windows.Forms.Timer _highlightTimer = null!;
        private Form?   _consoleDockForm;

        // ── Construction ─────────────────────────────────────────────────────

        public MainForm()
        {
            BuildUI();
            SetTitle();
            LoadDefaultContent();
        }

        // ── UI construction ───────────────────────────────────────────────────

        private void BuildUI()
        {
            SuspendLayout();

            Text            = "Caca Studio";
            Size            = new Size(1100, 760);
            MinimumSize     = new Size(700, 500);
            StartPosition   = FormStartPosition.CenterScreen;
            BackColor       = CSidebar;
            ForeColor       = CDefault;
            Font            = new Font("Segoe UI", 9f, FontStyle.Regular);

            BuildEditor();
            BuildStatusBar();
            BuildMenu();
            BuildToolbar();
            BuildHighlightTimer();

            ResumeLayout(false);
            PerformLayout();
        }

        // ── Menu ──────────────────────────────────────────────────────────────

        private void BuildMenu()
        {
            _menu = new MenuStrip { Renderer = new DarkRenderer() };

            var file  = AddMenu(_menu, "&File");
            AddItem(file, "&New",          "Ctrl+N", (_, _) => New());
            AddItem(file, "&Open…",        "Ctrl+O", (_, _) => Open());
            file.DropDownItems.Add(new ToolStripSeparator());
            var examples = new ToolStripMenuItem("Load E&xample")
            {
                ForeColor = Color.FromArgb(0xCC, 0xCC, 0xCC),
                BackColor = Color.FromArgb(0x2D, 0x2D, 0x30),
            };
            AddItem(examples, "&Hello World",  "", (_, _) => LoadExample(ExampleHelloWorld));
            AddItem(examples, "&Calculator",   "", (_, _) => LoadExample(ExampleCalculator));
            file.DropDownItems.Add(examples);
            file.DropDownItems.Add(new ToolStripSeparator());
            AddItem(file, "&Save",         "Ctrl+S", (_, _) => Save());
            AddItem(file, "Save &As…",     "",       (_, _) => SaveAs());
            file.DropDownItems.Add(new ToolStripSeparator());
            AddItem(file, "E&xit",         "Alt+F4", (_, _) => Close());

            var edit  = AddMenu(_menu, "&Edit");
            AddItem(edit, "&Undo",         "Ctrl+Z", (_, _) => _editor.Undo());
            AddItem(edit, "&Redo",         "Ctrl+Y", (_, _) => _editor.Redo());
            edit.DropDownItems.Add(new ToolStripSeparator());
            AddItem(edit, "Cu&t",          "Ctrl+X", (_, _) => _editor.Cut());
            AddItem(edit, "&Copy",         "Ctrl+C", (_, _) => _editor.Copy());
            AddItem(edit, "&Paste",        "Ctrl+V", (_, _) => _editor.Paste());
            edit.DropDownItems.Add(new ToolStripSeparator());
            AddItem(edit, "Select &All",   "Ctrl+A", (_, _) => _editor.SelectAll());

            var build = AddMenu(_menu, "&Build");
            AddItem(build, "&Compile",     "F5",     (_, _) => Compile());
            AddItem(build, "Compile && &Run", "F6",  (_, _) => CompileAndRun());
            AddItem(build, "&Debug",       "F7",     (_, _) => OpenDebugger());
            build.DropDownItems.Add(new ToolStripSeparator());
            AddItem(build, "&Decompile .ilc…", "",   (_, _) => OpenAndDecompile());

            var help  = AddMenu(_menu, "&Help");
            AddItem(help, "&About Caca Studio", "",   (_, _) => ShowAbout());

            StyleMenuItems(_menu);
            MainMenuStrip = _menu;
            Controls.Add(_menu);
        }

        private static ToolStripMenuItem AddMenu(MenuStrip bar, string text)
        {
            var item = new ToolStripMenuItem(text) { ForeColor = Color.FromArgb(0xCC, 0xCC, 0xCC) };
            bar.Items.Add(item);
            return item;
        }

        private static void AddItem(ToolStripMenuItem parent, string text, string shortcut, EventHandler handler)
        {
            var item = new ToolStripMenuItem(text)
            {
                ForeColor       = Color.FromArgb(0xCC, 0xCC, 0xCC),
                ShortcutKeyDisplayString = shortcut,
            };
            item.Click += handler;
            parent.DropDownItems.Add(item);
        }

        private static void StyleMenuItems(MenuStrip bar)
        {
            foreach (ToolStripItem top in bar.Items)
            {
                top.ForeColor = Color.FromArgb(0xCC, 0xCC, 0xCC);
                if (top is ToolStripMenuItem mi)
                    StyleDropDown(mi);
            }
        }

        private static void StyleDropDown(ToolStripMenuItem parent)
        {
            parent.DropDown.BackColor = Color.FromArgb(0x2D, 0x2D, 0x30);
            foreach (ToolStripItem child in parent.DropDownItems)
            {
                child.ForeColor = Color.FromArgb(0xCC, 0xCC, 0xCC);
                child.BackColor = Color.FromArgb(0x2D, 0x2D, 0x30);
                if (child is ToolStripMenuItem mi)
                    StyleDropDown(mi);
            }
        }

        // ── Toolbar ───────────────────────────────────────────────────────────

        private void BuildToolbar()
        {
            _toolbar = new ToolStrip
            {
                Renderer  = new DarkRenderer(),
                BackColor = CToolbar,
                GripStyle = ToolStripGripStyle.Hidden,
                Padding   = new Padding(4, 2, 0, 2),
                ImageScalingSize = new Size(16, 16),
            };

            AddTool(CreateIcon("📄"), "New file (Ctrl+N)",         () => New());
            AddTool(CreateIcon("📁"), "Open source file (Ctrl+O)", () => Open());
            AddTool(CreateIcon("💾"), "Save file (Ctrl+S)",        () => Save());
            _toolbar.Items.Add(new ToolStripSeparator());
            AddTool(CreateIcon("⚙️"), "Compile to .ilc (F5)",      () => Compile());
            AddTool(CreateIcon("▶️"), "Compile and run (F6)",      () => CompileAndRun());
            _toolbar.Items.Add(new ToolStripSeparator());
            AddTool(CreateIcon("📋"), "Open & decompile a .ilc binary", () => OpenAndDecompile());
            _toolbar.Items.Add(new ToolStripSeparator());
            AddTool(CreateIcon("🐛"), "Compile and debug step-by-step (F7)", () => OpenDebugger());
            _toolbar.Items.Add(new ToolStripSeparator());
            AddTool(CreateIcon("ℹ️"), "About Caca Studio",          () => ShowAbout());

            Controls.Add(_toolbar);
        }

        private static Bitmap CreateIcon(string emoji)
        {
            var bmp = new Bitmap(16, 16);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                using (var font = new Font("Segoe UI Emoji", 11f, FontStyle.Regular))
                {
                    var size = g.MeasureString(emoji, font);
                    var x = (16 - size.Width) / 2;
                    var y = (16 - size.Height) / 2;
                    g.DrawString(emoji, font, Brushes.White, x, y);
                }
            }
            return bmp;
        }

        private void AddTool(Bitmap icon, string tooltip, Action action)
        {
            var btn = new ToolStripButton
            {
                Image           = icon,
                ToolTipText     = tooltip,
                DisplayStyle    = ToolStripItemDisplayStyle.Image,
                BackColor       = CToolbar,
                AutoSize        = false,
                Width           = 24,
                Height          = 24,
            };
            btn.Click += (_, _) => action();
            _toolbar.Items.Add(btn);
        }

        // ── Editor + line numbers ─────────────────────────────────────────────

        private void BuildEditor()
        {
            // Outer split: editor (top) / output (bottom)
            _split = new SplitContainer
            {
                Dock            = DockStyle.Fill,
                Orientation     = Orientation.Horizontal,
                SplitterWidth   = 4,
                BackColor       = Color.FromArgb(0x3C, 0x3C, 0x3C),
                Panel1MinSize   = 80,
                Panel2MinSize   = 60,
            };

            // ── Editor panel (line numbers + code) ────────────────────────────
            var editorPanel = new Panel { Dock = DockStyle.Fill, BackColor = CBackground };

            _editor = new CodeEditor
            {
                Dock            = DockStyle.Fill,
                BackColor       = CBackground,
                ForeColor       = CDefault,
                Font            = PickMonoFont(10f),
                AcceptsTab      = true,
                WordWrap        = false,
                ScrollBars      = RichTextBoxScrollBars.Both,
                BorderStyle     = BorderStyle.None,
                DetectUrls      = false,
                HideSelection   = false,
            };
            _editor.TextChanged += OnTextChanged;
            _editor.SelectionChanged += OnSelectionChanged;
            _editor.Scroll += (_, _) => _lineNums.Invalidate();
            _editor.KeyDown += OnEditorKeyDown;

            _lineNums = new LineNumberPanel(_editor)
            {
                Dock  = DockStyle.Left,
                Width = 44,
            };

            // Thin separator between line numbers and editor
            var sep = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Color.FromArgb(0x3C, 0x3C, 0x3C) };

            editorPanel.Controls.Add(_editor);
            editorPanel.Controls.Add(sep);
            editorPanel.Controls.Add(_lineNums);
            _split.Panel1.Controls.Add(editorPanel);

            // ── Output panel ──────────────────────────────────────────────────
            _outputPanel = new Panel { Dock = DockStyle.Fill, BackColor = CSidebar };

            var headerPanel = new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 22,
                BackColor = CToolbar,
            };
            var outputLabel = new Label
            {
                Text      = "  Output",
                Dock      = DockStyle.Fill,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(0xAA, 0xAA, 0xAA),
                Font      = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
            };
            _undockBtn = new Button
            {
                Text      = "⊡",
                Dock      = DockStyle.Right,
                Width     = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = CToolbar,
                ForeColor = Color.FromArgb(0xAA, 0xAA, 0xAA),
                Cursor    = Cursors.Hand,
                Font      = new Font("Segoe UI", 8f, FontStyle.Regular),
                TabStop   = false,
                UseVisualStyleBackColor = false,
            };
            _undockBtn.FlatAppearance.BorderSize       = 0;
            _undockBtn.FlatAppearance.MouseOverBackColor = Color.FromArgb(0x3F, 0x3F, 0x46);
            _undockBtn.FlatAppearance.MouseDownBackColor = Color.FromArgb(0x00, 0x7A, 0xCC);
            _undockBtn.Click += (_, _) => ToggleConsoleDock();

            _undockTip = new ToolTip();
            _undockTip.SetToolTip(_undockBtn, "Pop out to separate window");

            headerPanel.Controls.Add(outputLabel);
            headerPanel.Controls.Add(_undockBtn);

            _output = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = Color.FromArgb(0x12, 0x12, 0x12),
                ForeColor   = CDefault,
                Font        = PickMonoFont(9f),
                ReadOnly    = true,
                BorderStyle = BorderStyle.None,
                WordWrap    = false,
                DetectUrls  = false,
                ScrollBars  = RichTextBoxScrollBars.Both,
            };

            _outputPanel.Controls.Add(_output);
            _outputPanel.Controls.Add(headerPanel);
            _split.Panel2.Controls.Add(_outputPanel);
            _split.SplitterDistance = 480;

            Controls.Add(_split);
        }

        // ── Status bar ────────────────────────────────────────────────────────

        private void BuildStatusBar()
        {
            _status = new StatusStrip
            {
                BackColor       = CToolbar,
                ForeColor       = Color.FromArgb(0xAA, 0xAA, 0xAA),
                SizingGrip      = false,
                Renderer        = new DarkRenderer(),
            };

            _statusFile = new ToolStripStatusLabel("New file")
            {
                ForeColor  = Color.FromArgb(0xAA, 0xAA, 0xAA),
                Spring     = true,
                TextAlign  = ContentAlignment.MiddleLeft,
            };
            _statusPos = new ToolStripStatusLabel("Ln 1, Col 1")
            {
                ForeColor  = Color.FromArgb(0x85, 0x85, 0x85),
                Alignment  = ToolStripItemAlignment.Right,
            };
            _status.Items.Add(_statusFile);
            _status.Items.Add(_statusPos);
            Controls.Add(_status);
        }

        // ── Highlight timer ───────────────────────────────────────────────────

        private void BuildHighlightTimer()
        {
            _highlightTimer = new System.Windows.Forms.Timer { Interval = 250 };
            _highlightTimer.Tick += (_, _) =>
            {
                _highlightTimer.Stop();
                ApplySyntaxHighlighting();
            };
        }

        // ── Keyboard shortcuts ────────────────────────────────────────────────

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.N: New();    return true;
                case Keys.Control | Keys.O: Open();   return true;
                case Keys.Control | Keys.S: Save();   return true;
                case Keys.F5: Compile();              return true;
                case Keys.F6: CompileAndRun();        return true;
                case Keys.F7: OpenDebugger();         return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void OnEditorKeyDown(object? sender, KeyEventArgs e)
        {
            // Auto-indent: preserve leading whitespace on Enter
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                int lineIdx = _editor.GetLineFromCharIndex(_editor.SelectionStart);
                string curLine = lineIdx < _editor.Lines.Length ? _editor.Lines[lineIdx] : "";
                string indent = "";
                foreach (char c in curLine)
                {
                    if (c == ' ' || c == '\t') indent += c;
                    else break;
                }
                _editor.SelectedText = "\n" + indent;
            }
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void OnTextChanged(object? sender, EventArgs e)
        {
            if (_highlighting) return;
            _modified = true;
            SetTitle();
            _highlightTimer.Stop();
            _highlightTimer.Start();
            _lineNums.Invalidate();
        }

        private void OnSelectionChanged(object? sender, EventArgs e)
        {
            int idx  = _editor.SelectionStart;
            int line = _editor.GetLineFromCharIndex(idx) + 1;
            int col  = idx - _editor.GetFirstCharIndexFromLine(line - 1) + 1;
            _statusPos.Text = $"Ln {line}, Col {col}";
        }

        // ── File operations ───────────────────────────────────────────────────

        private void New()
        {
            if (!ConfirmDiscard()) return;
            _editor.Clear();
            _filePath = string.Empty;
            _modified = false;
            _lastBuild = Array.Empty<byte>();
            ClearOutput();
            SetTitle();
            LoadDefaultContent();
        }

        private void Open()
        {
            if (!ConfirmDiscard()) return;
            using var dlg = new OpenFileDialog
            {
                Title  = "Open CIL Source",
                Filter = "CIL Source (*.cil;*.asm)|*.cil;*.asm|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            _editor.Text = File.ReadAllText(dlg.FileName);
            _filePath    = dlg.FileName;
            _modified    = false;
            _lastBuild   = Array.Empty<byte>();
            SetTitle();
            ApplySyntaxHighlighting();
        }

        private void Save()
        {
            if (string.IsNullOrEmpty(_filePath)) { SaveAs(); return; }
            File.WriteAllText(_filePath, _editor.Text);
            _modified = false;
            SetTitle();
        }

        private void SaveAs()
        {
            using var dlg = new SaveFileDialog
            {
                Title      = "Save CIL Source",
                Filter     = "CIL Source (*.cil)|*.cil|Assembly (*.asm)|*.asm|All files (*.*)|*.*",
                DefaultExt = "ail",
                FileName   = string.IsNullOrEmpty(_filePath) ? "program" : Path.GetFileName(_filePath),
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            File.WriteAllText(dlg.FileName, _editor.Text);
            _filePath = dlg.FileName;
            _modified = false;
            SetTitle();
        }

        // ── Build / run ───────────────────────────────────────────────────────

        private void Compile()
        {
            ClearOutput();
            AppendOutput("── Compiling… ──────────────────────\n", COutputInfo);
            try
            {
                var c = new Compiler.Compiler(_editor.Text);
                _lastBuild = c.Compile();
                AppendOutput($"✓ Compiled OK — {_lastBuild.Length / 6} instruction(s), " +
                             $"{_lastBuild.Length} byte(s).\n", COutputSuccess);

                using var dlg = new SaveFileDialog
                {
                    Title      = "Save compiled binary",
                    Filter     = "CacaVM binary (*.ilc)|*.ilc|All files (*.*)|*.*",
                    DefaultExt = "ila",
                    FileName   = string.IsNullOrEmpty(_filePath)
                        ? "output"
                        : Path.GetFileNameWithoutExtension(_filePath),
                };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    byte[] ilc = WrapInIlc(_lastBuild);
                    File.WriteAllBytes(dlg.FileName, ilc);
                    AppendOutput($"  Saved → {dlg.FileName}\n", COutputInfo);
                }
            }
            catch (Compiler.BuildException ex)
            {
                AppendOutput($"✗ Error on line {ex.SrcLineNumber}: {ex.Message}\n", COutputError);
            }
            catch (Exception ex)
            {
                AppendOutput($"✗ {ex.Message}\n", COutputError);
            }
        }

        private void CompileAndRun()
        {
            ClearOutput();
            AppendOutput("── Compile & Run ────────────────────\n", COutputInfo);
            try
            {
                var c = new Compiler.Compiler(_editor.Text);
                _lastBuild = c.Compile();
                AppendOutput($"✓ Compiled — {_lastBuild.Length / 6} instruction(s).\n", COutputSuccess);
                AppendOutput("── VM output ────────────────────────\n", COutputInfo);
                RunAsync(_lastBuild);
            }
            catch (Compiler.BuildException ex)
            {
                AppendOutput($"✗ Error on line {ex.SrcLineNumber}: {ex.Message}\n", COutputError);
            }
            catch (Exception ex)
            {
                AppendOutput($"✗ {ex.Message}\n", COutputError);
            }
        }

        private void RunAsync(byte[] code)
        {
            Globals.console = new StudioConsole(_output);
            Globals.DebugMode = false;

            Task.Run(() =>
            {
                try
                {
                    Executable.Run(code);
                    _output.Invoke((Action)(() =>
                        AppendOutput("\n── Done ─────────────────────────────\n", COutputInfo)));
                }
                catch (Exception ex)
                {
                    _output.Invoke((Action)(() =>
                        AppendOutput($"\n✗ Runtime error: {ex.Message}\n", COutputError)));
                }
            });
        }

        private void OpenDebugger()
        {
            ClearOutput();
            AppendOutput("── Debug ────────────────────────────\n", COutputInfo);
            try
            {
                var c = new Compiler.Compiler(_editor.Text);
                byte[] code = c.Compile();
                AppendOutput($"✓ Compiled — {code.Length / 6} instruction(s).\n", COutputSuccess);
                var dbg = new DebugForm(code);
                dbg.Show(this);
            }
            catch (Compiler.BuildException ex)
            {
                AppendOutput($"✗ Error on line {ex.SrcLineNumber}: {ex.Message}\n", COutputError);
            }
            catch (Exception ex)
            {
                AppendOutput($"✗ {ex.Message}\n", COutputError);
            }
        }

        private void OpenAndDecompile()
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "Open .ilc binary to decompile",
                Filter = "CacaVM binary (*.ilc)|*.ilc|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            if (!ConfirmDiscard()) return;

            try
            {
                byte[] bin  = File.ReadAllBytes(dlg.FileName);
                var decomp  = new Decompiler.Decompiler(bin);
                string asm  = decomp.Decompile();
                _editor.Text = asm;
                _filePath    = string.Empty;
                _modified    = false;
                SetTitle();
                ClearOutput();
                AppendOutput($"✓ Decompiled {bin.Length} byte(s) from {Path.GetFileName(dlg.FileName)}\n",
                             COutputSuccess);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Decompile Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ── Console undocking ─────────────────────────────────────────────────

        private void ToggleConsoleDock()
        {
            if (_consoleDockForm == null)
                UndockConsole();
            else
                ReDockConsole();
        }

        private void UndockConsole()
        {
            _split.Panel2.Controls.Remove(_outputPanel);
            _split.Panel2Collapsed = true;

            var f = new Form
            {
                Text            = "Caca Studio – Console",
                Size            = new Size(700, 380),
                MinimumSize     = new Size(300, 200),
                StartPosition   = FormStartPosition.Manual,
                BackColor       = Color.FromArgb(0x1E, 0x1E, 0x1E),
                ForeColor       = Color.FromArgb(0xCC, 0xCC, 0xCC),
                Font            = new Font("Segoe UI", 9f, FontStyle.Regular),
            };
            f.Location = new Point(Right + 4, Top);
            // Keep the window within the working area of the current screen
            var wa = Screen.FromHandle(Handle).WorkingArea;
            int fx = Math.Min(f.Left, wa.Right  - f.Width);
            int fy = Math.Min(f.Top,  wa.Bottom - f.Height);
            f.Location = new Point(Math.Max(fx, wa.Left), Math.Max(fy, wa.Top));
            _outputPanel.Dock = DockStyle.Fill;
            f.Controls.Add(_outputPanel);

            _consoleDockForm = f;
            _undockBtn.Text = "⊞";
            _undockTip.SetToolTip(_undockBtn, "Dock back to main window");

            f.FormClosing += (_, _) =>
            {
                if (_consoleDockForm != f) return;
                if (IsDisposed || Disposing || _split.IsDisposed) return;
                _consoleDockForm = null;
                if (f.Controls.Contains(_outputPanel))
                    f.Controls.Remove(_outputPanel);
                _outputPanel.Dock = DockStyle.Fill;
                _split.Panel2.Controls.Add(_outputPanel);
                _split.Panel2Collapsed = false;
                _undockBtn.Text = "⊡";
                _undockTip.SetToolTip(_undockBtn, "Pop out to separate window");
            };

            f.Show(this);
        }

        private void ReDockConsole()
        {
            _consoleDockForm?.Close();   // FormClosing handler does the actual redock
        }

        // ── Load example ──────────────────────────────────────────────────────

        private void LoadExample(string content)
        {
            if (!ConfirmDiscard()) return;
            _filePath  = string.Empty;
            _lastBuild = Array.Empty<byte>();
            ClearOutput();
            _editor.Text = content;
            _modified = false;
            SetTitle();
            _highlightTimer.Stop();
            ApplySyntaxHighlighting();
        }

        private static readonly string ExampleHelloWorld =
@"; hello_world_db.cil
; Demonstrates the DB pseudo-instruction (like the x86 `db` directive).
; The string ""Hello, World"" is defined inline in the source; the program
; then prints it using KEI 0x01 in write-string mode.

        JMP     main            ; skip over data section

; ── Data section ──────────────────────────────────────────────────────────────
hello:
        DB      ""Hello, World"", 0x0A, 0x00   ; 14 bytes: text + newline + null

; ── Code section ──────────────────────────────────────────────────────────────
main:
        MOV     AL, 0x02        ; KEI 0x01 mode: write string from memory
        MOV     X, hello        ; X = address of string data
        MOV     BL, 13          ; BL = length of string (13 bytes)
        KEI     0x01            ; write string to stdout
        KEI     0x02            ; halt
";

        private static readonly string ExampleCalculator =
@"; calculator.cil
; Simple integer calculator demonstrating CIL arithmetic.
; Each block prints   A op B = result
; using KEI 0x01 (write-char / write-integer) interrupts.
;
; KEI 0x01 modes used:
;   AL = 0x01  write a single character (value in AH)
;   AL = 0x05  write register B as a decimal integer

; ── 3 + 4 = 7 ────────────────────────────────────────────────────────────────
        MOV     AL, 0x01
        MOV     AH, '3'
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     AH, '+'
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     AH, '4'
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     AH, '='
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     BL, 3
        ADD     BL, 4           ; BL = 7
        MOV     AL, 0x05
        KEI     0x01            ; print ""7""
        MOV     AL, 0x01
        MOV     AH, 0x0A        ; newline
        KEI     0x01

; ── 10 - 3 = 7 ───────────────────────────────────────────────────────────────
        MOV     AH, '1'
        KEI     0x01
        MOV     AH, '0'
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     AH, '-'
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     AH, '3'
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     AH, '='
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     BL, 10
        SUB     BL, 3           ; BL = 7
        MOV     AL, 0x05
        KEI     0x01            ; print ""7""
        MOV     AL, 0x01
        MOV     AH, 0x0A
        KEI     0x01

; ── 6 * 7 = 42 ───────────────────────────────────────────────────────────────
        MOV     AH, '6'
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     AH, '*'
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     AH, '7'
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     AH, '='
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     BL, 6
        MUL     BL, 7           ; BL = 42
        MOV     AL, 0x05
        KEI     0x01            ; print ""42""
        MOV     AL, 0x01
        MOV     AH, 0x0A
        KEI     0x01

; ── 20 / 4 = 5 ───────────────────────────────────────────────────────────────
        MOV     AH, '2'
        KEI     0x01
        MOV     AH, '0'
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     AH, '/'
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     AH, '4'
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     AH, '='
        KEI     0x01
        MOV     AH, ' '
        KEI     0x01
        MOV     BL, 20
        DIV     BL, 4           ; BL = 5
        MOV     AL, 0x05
        KEI     0x01            ; print ""5""
        MOV     AL, 0x01
        MOV     AH, 0x0A
        KEI     0x01

        KEI     0x02            ; halt
";

        // Known mnemonics for colouring
        private static readonly string[] Mnemonics =
        {
            "MOV","MOM","MOE","SWP","TEQ","TNE","TLT","TMT",
            "ADD","SUB","INC","DEC","MUL","DIV",
            "SHL","SHR","ROL","ROR","AND","BOR","XOR","NOT",
            "JMP","CLL","RET","JMT","JMF","CLT","CLF",
            "PSH","POP",
            "INB","INW","IND","OUB","OUW","OUD",
            "SWI","KEI",
        };

        // Pre-compiled patterns (case-insensitive)
        private static readonly Regex RxComment   = new(@"(//|;).*$",
            RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex RxLabel     = new(@"^\s*\w+\s*:",
            RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex RxMnemonic  = new(
            @"(?<!\w)(" + string.Join("|", Mnemonics) + @")(?!\w)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxRegister  = new(
            @"(?<!\w)(PC|IP|SP|SS|AH|AL|A|BH|BL|B|CH|CL|C|X|Y)(?!\w)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RxHex       = new(@"\b0x[0-9A-Fa-f]+\b",
            RegexOptions.Compiled);
        private static readonly Regex RxDecimal   = new(@"(?<!\w)\d+(?!\w)",
            RegexOptions.Compiled);
        private static readonly Regex RxCharLit   = new(@"'(\\.|[^\\'])'",
            RegexOptions.Compiled);

        private void ApplySyntaxHighlighting()
        {
            if (_highlighting || _editor.TextLength == 0) return;
            _highlighting = true;

            int savedStart  = _editor.SelectionStart;
            int savedLength = _editor.SelectionLength;

            SendMessage(_editor.Handle, WM_SETREDRAW, false, 0);
            try
            {
                // Baseline — reset everything to default
                _editor.SelectAll();
                _editor.SelectionColor = CDefault;

                string text = _editor.Text;

                // Comments (highest priority — applied last so they win over mnemonics)
                ColourMatches(RxCharLit,  text, CNumber);
                ColourMatches(RxDecimal,  text, CNumber);
                ColourMatches(RxHex,      text, CNumber);
                ColourMatches(RxRegister, text, CRegister);
                ColourMatches(RxMnemonic, text, CMnemonic);
                ColourMatches(RxLabel,    text, CLabel);
                ColourMatches(RxComment,  text, CComment); // wins over everything
            }
            finally
            {
                _editor.SelectionStart  = savedStart;
                _editor.SelectionLength = savedLength;
                SendMessage(_editor.Handle, WM_SETREDRAW, true, 0);
                _editor.Invalidate();
                _highlighting = false;
            }
        }

        private void ColourMatches(Regex rx, string text, Color colour)
        {
            foreach (Match m in rx.Matches(text))
            {
                _editor.Select(m.Index, m.Length);
                _editor.SelectionColor = colour;
            }
        }

        // ── Output helpers ────────────────────────────────────────────────────

        private void ClearOutput()
        {
            _output.Clear();
        }

        private void AppendOutput(string text, Color colour)
        {
            int start = _output.TextLength;
            _output.AppendText(text);
            _output.Select(start, text.Length);
            _output.SelectionColor = colour;
            _output.SelectionLength = 0;
            _output.ScrollToCaret();
        }

        // ── Misc helpers ──────────────────────────────────────────────────────

        private bool ConfirmDiscard()
        {
            if (!_modified) return true;
            var r = MessageBox.Show(
                "You have unsaved changes. Discard them?",
                "Caca Studio", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            return r == DialogResult.Yes;
        }

        private void SetTitle()
        {
            string name = string.IsNullOrEmpty(_filePath)
                ? "Untitled"
                : Path.GetFileName(_filePath);
            Text = $"{(_modified ? "● " : "")}{name} — Caca Studio";
            _statusFile.Text = string.IsNullOrEmpty(_filePath) ? "  New file" : $"  {_filePath}";
        }

        private void LoadDefaultContent()
        {
            _editor.Text =
                "// Caca Studio — Caca Intermediate Language\n" +
                "// Example: print 'Hi!' then halt\n" +
                "\n" +
                "MOV AL, 0x01   // write-char mode\n" +
                "MOV AH, 'H'\n" +
                "KEI 0x01\n" +
                "MOV AH, 'i'\n" +
                "KEI 0x01\n" +
                "MOV AH, '!'\n" +
                "KEI 0x01\n" +
                "MOV AH, '\\n'\n" +
                "KEI 0x01\n" +
                "KEI 0x02       // halt\n";
            _modified = false;
            SetTitle();
            _highlightTimer.Stop();
            ApplySyntaxHighlighting();
        }

        private void ShowAbout()
        {
            using var dlg = new Form
            {
                Text            = "About Caca Studio",
                Width           = 360,
                Height          = 200,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition   = FormStartPosition.CenterParent,
                MaximizeBox     = false, MinimizeBox = false,
                BackColor       = CSidebar,
                ForeColor       = CDefault,
            };
            var lbl = new Label
            {
                Text      = "Caca Studio\n\n" +
                            "IDE for Caca Intermediate Language\n" +
                            "Assembler · Decompiler · Run\n\n" +
                            ".NET 8 · WinForms · No external dependencies",
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = CDefault,
                BackColor = Color.Transparent,
            };
            var ok = new Button
            {
                Text         = "OK",
                DialogResult = DialogResult.OK,
                Left         = 135, Top = 148, Width = 80,
                BackColor    = Color.FromArgb(0x0E, 0x63, 0x9D),
                ForeColor    = Color.White,
                FlatStyle    = FlatStyle.Flat,
            };
            ok.FlatAppearance.BorderSize = 0;
            dlg.Controls.AddRange(new Control[] { lbl, ok });
            dlg.AcceptButton = ok;
            dlg.ShowDialog();
        }

        private static Font PickMonoFont(float size)
        {
            foreach (string name in new[] { "Cascadia Code", "Consolas", "Courier New" })
            {
                try
                {
                    var f = new Font(name, size, FontStyle.Regular, GraphicsUnit.Point);
                    if (f.Name == name) return f;
                    f.Dispose();
                }
                catch { /* try next */ }
            }
            return new Font(FontFamily.GenericMonospace, size);
        }

        /// <summary>Wraps raw bytecode in a minimal .ilc container (§8).</summary>
        private static byte[] WrapInIlc(byte[] code)
        {
            using var ms = new System.IO.MemoryStream();
            // Magic: "CIL\0"
            ms.Write(new byte[] { 0x43, 0x49, 0x4C, 0x00 });
            // Version 2 (LE)
            ms.Write(new byte[] { 0x02, 0x00 });
            // Section count = 1 (LE)
            ms.Write(new byte[] { 0x01, 0x00 });
            // Section type = 0x0001 code (LE)
            ms.Write(new byte[] { 0x01, 0x00 });
            // Section length (LE)
            ms.Write(BitConverter.GetBytes(code.Length));
            // Section data
            ms.Write(code);
            return ms.ToArray();
        }

        // ── Closing ───────────────────────────────────────────────────────────

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!ConfirmDiscard()) e.Cancel = true;
            base.OnFormClosing(e);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Custom RichTextBox — exposes Scroll event for line-number sync
    // ═════════════════════════════════════════════════════════════════════════

    internal sealed class CodeEditor : RichTextBox
    {
        public event EventHandler? Scroll;

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // WM_VSCROLL | WM_MOUSEWHEEL | WM_KEYDOWN
            if (m.Msg == 0x0115 || m.Msg == 0x020A || m.Msg == 0x0100)
                Scroll?.Invoke(this, EventArgs.Empty);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Line number gutter
    // ═════════════════════════════════════════════════════════════════════════

    internal sealed class LineNumberPanel : Panel
    {
        private readonly RichTextBox _editor;
        private static readonly Color BgColor = Color.FromArgb(0x25, 0x25, 0x26);
        private static readonly Color FgColor = Color.FromArgb(0x85, 0x85, 0x85);
        private static readonly Color CurBg   = Color.FromArgb(0x2A, 0x2D, 0x31);
        private static readonly Color CurFg   = Color.FromArgb(0xC6, 0xC6, 0xC6);

        public LineNumberPanel(RichTextBox editor)
        {
            _editor     = editor;
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BgColor);

            if (_editor.Lines.Length == 0) return;

            int curLine = _editor.GetLineFromCharIndex(_editor.SelectionStart);
            int firstCharIdx = _editor.GetCharIndexFromPosition(new Point(0, 0));
            int firstLine    = _editor.GetLineFromCharIndex(firstCharIdx);
            int lastCharIdx  = _editor.GetCharIndexFromPosition(new Point(0, _editor.ClientSize.Height - 1));
            int lastLine     = _editor.GetLineFromCharIndex(lastCharIdx);
            int total        = _editor.Lines.Length;

            using var fgBrush  = new SolidBrush(FgColor);
            using var curBrush = new SolidBrush(CurFg);
            using var curBg    = new SolidBrush(CurBg);

            for (int i = firstLine; i <= Math.Min(lastLine + 1, total - 1); i++)
            {
                int charIdx = _editor.GetFirstCharIndexFromLine(i);
                if (charIdx < 0) continue;
                Point pos = _editor.GetPositionFromCharIndex(charIdx);

                string num = (i + 1).ToString();
                SizeF  sz  = e.Graphics.MeasureString(num, _editor.Font);

                bool isCurrent = (i == curLine);
                if (isCurrent)
                    e.Graphics.FillRectangle(curBg, 0, pos.Y, Width, (int)sz.Height + 1);

                e.Graphics.DrawString(num, _editor.Font,
                    isCurrent ? curBrush : fgBrush,
                    Width - sz.Width - 4, pos.Y);
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Dark ToolStrip / MenuStrip renderer
    // ═════════════════════════════════════════════════════════════════════════

    internal sealed class DarkRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color Bg        = Color.FromArgb(0x2D, 0x2D, 0x30);
        private static readonly Color Hover     = Color.FromArgb(0x3F, 0x3F, 0x46);
        private static readonly Color Pressed   = Color.FromArgb(0x00, 0x7A, 0xCC);
        private static readonly Color Border    = Color.FromArgb(0x3F, 0x3F, 0x46);
        private static readonly Color Separator = Color.FromArgb(0x3F, 0x3F, 0x46);
        private static readonly Color DropBg    = Color.FromArgb(0x1B, 0x1B, 0x1C);

        public DarkRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            e.Graphics.FillRectangle(new SolidBrush(Bg), e.AffectedBounds);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            var bg   = item.Selected || item.Pressed ? Hover : Bg;
            if (e.Item.OwnerItem == null && e.Item.Selected)
                bg = Hover;
            e.Graphics.FillRectangle(new SolidBrush(bg), new Rectangle(Point.Empty, item.Size));
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var btn = (ToolStripButton)e.Item;
            Color bg = btn.Pressed ? Pressed : btn.Selected ? Hover : Bg;
            e.Graphics.FillRectangle(new SolidBrush(bg), new Rectangle(Point.Empty, btn.Size));
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (e.Vertical)
            {
                int x = e.Item.Width / 2;
                e.Graphics.DrawLine(new Pen(Separator), x, 3, x, e.Item.Height - 3);
            }
            else
            {
                int y = e.Item.Height / 2;
                e.Graphics.DrawLine(new Pen(Separator), 5, y, e.Item.Width - 5, y);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // No border on toolstrip
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            e.Graphics.FillRectangle(new SolidBrush(DropBg), e.AffectedBounds);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.FromArgb(0xCC, 0xCC, 0xCC);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Color.FromArgb(0x99, 0x99, 0x99);
            base.OnRenderArrow(e);
        }
    }

    internal sealed class DarkColorTable : ProfessionalColorTable
    {
        private static readonly Color Bg   = Color.FromArgb(0x2D, 0x2D, 0x30);
        private static readonly Color Drop = Color.FromArgb(0x1B, 0x1B, 0x1C);
        private static readonly Color Brd  = Color.FromArgb(0x3F, 0x3F, 0x46);
        private static readonly Color Sel  = Color.FromArgb(0x3F, 0x3F, 0x46);

        public override Color MenuBorder                          => Brd;
        public override Color MenuItemBorder                      => Brd;
        public override Color MenuItemSelected                    => Sel;
        public override Color MenuItemSelectedGradientBegin       => Sel;
        public override Color MenuItemSelectedGradientEnd         => Sel;
        public override Color MenuItemPressedGradientBegin        => Color.FromArgb(0x00, 0x7A, 0xCC);
        public override Color MenuItemPressedGradientEnd          => Color.FromArgb(0x00, 0x7A, 0xCC);
        public override Color ToolStripDropDownBackground         => Drop;
        public override Color ToolStripGradientBegin              => Bg;
        public override Color ToolStripGradientEnd                => Bg;
        public override Color ToolStripGradientMiddle             => Bg;
        public override Color ImageMarginGradientBegin            => Drop;
        public override Color ImageMarginGradientMiddle           => Drop;
        public override Color ImageMarginGradientEnd              => Drop;
        public override Color StatusStripGradientBegin            => Bg;
        public override Color StatusStripGradientEnd              => Bg;
        public override Color SeparatorDark                       => Brd;
        public override Color SeparatorLight                      => Brd;
        public override Color CheckBackground                     => Color.FromArgb(0x00, 0x7A, 0xCC);
        public override Color CheckSelectedBackground             => Color.FromArgb(0x00, 0x7A, 0xCC);
        public override Color CheckPressedBackground              => Color.FromArgb(0x00, 0x7A, 0xCC);
        public override Color ButtonSelectedBorder                => Brd;
        public override Color ButtonSelectedHighlight             => Sel;
        public override Color ButtonSelectedHighlightBorder       => Brd;
        public override Color ButtonPressedBorder                 => Color.FromArgb(0x00, 0x7A, 0xCC);
        public override Color ButtonPressedHighlight              => Color.FromArgb(0x00, 0x7A, 0xCC);
        public override Color ButtonCheckedGradientBegin          => Color.FromArgb(0x00, 0x7A, 0xCC);
        public override Color ButtonCheckedGradientEnd            => Color.FromArgb(0x00, 0x7A, 0xCC);
        public override Color ButtonCheckedHighlight              => Color.FromArgb(0x00, 0x7A, 0xCC);
        public override Color ButtonCheckedHighlightBorder        => Brd;
    }
}
