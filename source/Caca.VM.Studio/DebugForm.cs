using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Caca.VM;

namespace Caca.VM.Studio
{
    /// <summary>
    /// Step-through debugger for CacaVM programs.
    ///
    /// Layout:
    ///   ToolStrip   — Step / Run / Pause / Stop / Speed / Steps counter
    ///   ┌─────────────┬───────────────────────────────────────┐
    ///   │  Registers  │  Code / Memory (6-byte rows, IP lit)  │
    ///   │  + Stack    ├───────────────────────────────────────┤
    ///   │             │  VM Output                            │
    ///   └─────────────┴───────────────────────────────────────┘
    ///   StatusStrip — halted/running · current decoded instruction
    /// </summary>
    public sealed class DebugForm : Form
    {
        // ── Win32 ─────────────────────────────────────────────────────────────
        [DllImport("user32.dll")] private static extern int SendMessage(IntPtr h, int m, bool w, int l);
        private const int WM_SETREDRAW = 11;

        // ── Colours (consistent with MainForm) ────────────────────────────────
        private static readonly Color CBg          = Color.FromArgb(0x1E, 0x1E, 0x1E);
        private static readonly Color CSide        = Color.FromArgb(0x25, 0x25, 0x26);
        private static readonly Color CToolbar     = Color.FromArgb(0x2D, 0x2D, 0x30);
        private static readonly Color CDefault     = Color.FromArgb(0xD4, 0xD4, 0xD4);
        private static readonly Color CRegName     = Color.FromArgb(0x9C, 0xDC, 0xFE);
        private static readonly Color CRegVal      = Color.FromArgb(0xCE, 0x91, 0x78);
        private static readonly Color CRegChanged  = Color.FromArgb(0xF4, 0xC8, 0x42);
        private static readonly Color CMnemonic    = Color.FromArgb(0x56, 0x9C, 0xD6);
        private static readonly Color CAddr        = Color.FromArgb(0x85, 0x85, 0x85);
        private static readonly Color CByte        = Color.FromArgb(0xD4, 0xD4, 0xD4);
        private static readonly Color CIpRow       = Color.FromArgb(0x26, 0x4F, 0x78);
        private static readonly Color CIpRowFg     = Color.FromArgb(0xFF, 0xFF, 0xFF);
        private static readonly Color CSuccess     = Color.FromArgb(0x6A, 0x99, 0x55);
        private static readonly Color CError       = Color.FromArgb(0xF4, 0x47, 0x47);
        private static readonly Color CInfo        = Color.FromArgb(0x9C, 0xDC, 0xFE);
        private static readonly Color CSectionHdr  = Color.FromArgb(0x3C, 0x3C, 0x3C);

        // ── VM state ──────────────────────────────────────────────────────────
        private readonly byte[] _code;
        private VM _vm = null!;
        private bool _halted = false;
        private int  _steps  = 0;

        // Previous register snapshot for change detection
        private int[] _prevRegs = new int[15];

        // ── Controls ──────────────────────────────────────────────────────────
        private RichTextBox _regBox   = null!;
        private RichTextBox _memBox   = null!;
        private RichTextBox _outBox   = null!;
        private ToolStripButton _stepBtn  = null!;
        private ToolStripButton _runBtn   = null!;
        private ToolStripButton _pauseBtn = null!;
        private ToolStripButton _stopBtn  = null!;
        private TrackBar      _speedBar  = null!;
        private ToolStripStatusLabel _statusState   = null!;
        private ToolStripStatusLabel _statusInstr   = null!;
        private System.Windows.Forms.Timer _runTimer = null!;

        // ── Constructor ───────────────────────────────────────────────────────

        public DebugForm(byte[] rawBytecode)
        {
            _code = rawBytecode;
            BuildUI();
            InitVM();
            UpdateDisplay();
        }

        // ── VM lifecycle ──────────────────────────────────────────────────────

        private void InitVM()
        {
            _vm         = new VM(_code, Globals.DefaultRamSize);
            _vm.Running = true;
            _halted     = false;
            _steps      = 0;
            Globals.console  = new StudioConsole(_outBox);
            Globals.DebugMode = false;
            SnapshotRegs();
        }

        private bool IsHalted =>
            _halted || !_vm.Running || _vm.ram.memory[_vm.IP] == 0x00;

        // ── Step logic ────────────────────────────────────────────────────────

        private void DoStep()
        {
            if (IsHalted) { OnHalted(); return; }
            SnapshotRegs();   // capture before
            try
            {
                _vm.Tick();
            }
            catch (Exception ex)
            {
                AppendOutput($"\n✗ Runtime exception: {ex.Message}\n", CError);
                _halted = true;
            }
            _steps++;
            UpdateDisplay();
            if (IsHalted) OnHalted();
        }

        private void OnHalted()
        {
            _halted = true;
            _runTimer.Stop();
            _stepBtn.Enabled  = false;
            _runBtn.Enabled   = false;
            _pauseBtn.Enabled = false;
            SetStatusState("■ Halted", CError);
            AppendOutput("\n── Execution halted ─────────────────\n", CInfo);
        }

        private void SnapshotRegs()
        {
            _prevRegs[0]  = _vm.PC;
            _prevRegs[1]  = _vm.IP;
            _prevRegs[2]  = _vm.SP;
            _prevRegs[3]  = _vm.SS;
            _prevRegs[4]  = _vm.AL;
            _prevRegs[5]  = _vm.AH;
            _prevRegs[6]  = _vm.BL;
            _prevRegs[7]  = _vm.BH;
            _prevRegs[8]  = _vm.CL;
            _prevRegs[9]  = _vm.CH;
            _prevRegs[10] = _vm.X;
            _prevRegs[11] = _vm.Y;
        }
        // ── Display refresh ───────────────────────────────────────────────────

        private void UpdateDisplay()
        {
            UpdateRegisters();
            UpdateMemory();
            UpdateStatus();
            if (_stepsLabelHost != null) _stepsLabelHost.Text = $"Steps: {_steps}";
        }

        // ── Registers ─────────────────────────────────────────────────────────

        private void UpdateRegisters()
        {
            SendMessage(_regBox.Handle, WM_SETREDRAW, false, 0);
            _regBox.Clear();

            SectionHeader("  REGISTERS");

            // Two-column layout: name + value pairs
            RegRow("PC",  $"0x{_vm.PC:X2}",    _vm.PC  != _prevRegs[0],
                   "IP",  $"0x{_vm.IP:X2}",    _vm.IP  != _prevRegs[1]);
            RegRow("SP",  $"0x{_vm.SP:X2}",    _vm.SP  != _prevRegs[2],
                   "SS",  $"0x{_vm.SS:X2}",    _vm.SS  != _prevRegs[3]);

            _regBox.AppendText("\n");

            RegRow("AL",  $"0x{_vm.AL:X2}",    _vm.AL  != _prevRegs[4],
                   "AH",  $"0x{_vm.AH:X2}",    _vm.AH  != _prevRegs[5]);
            RegRow("BL",  $"0x{_vm.BL:X2}",    _vm.BL  != _prevRegs[6],
                   "BH",  $"0x{_vm.BH:X2}",    _vm.BH  != _prevRegs[7]);
            RegRow("CL",  $"0x{_vm.CL:X2}",    _vm.CL  != _prevRegs[8],
                   "CH",  $"0x{_vm.CH:X2}",    _vm.CH  != _prevRegs[9]);

            _regBox.AppendText("\n");

            // Wide 32-bit registers
            WideReg("X",  $"0x{_vm.X:X8}",    _vm.X   != _prevRegs[10]);
            WideReg("Y",  $"0x{_vm.Y:X8}",    _vm.Y   != _prevRegs[11]);

            // Derived 16-bit views
            int a = (_vm.AH << 8) | _vm.AL;
            int b = (_vm.BH << 8) | _vm.BL;
            int c = (_vm.CH << 8) | _vm.CL;
            _regBox.AppendText("\n");
            WideReg("A",  $"0x{a:X4}",  false);
            WideReg("B",  $"0x{b:X4}",  false);
            WideReg("C",  $"0x{c:X4}",  false);

            // ── Stack ──────────────────────────────────────────────────────────
            _regBox.AppendText("\n");
            SectionHeader("  STACK");
            // The stack lives at the top of the shared RAM segment (grows down from
            // ram.memory.Length - 1), not a fixed 256-byte range — SP/the stack top
            // can be well past two hex digits once ram is larger than 256 bytes.
            int sp = _vm.SP;
            int stackTop = _vm.ram.memory.Length - 1;
            if (sp >= stackTop)
            {
                Dim("  (empty)\n");
            }
            else
            {
                for (int i = sp; i <= stackTop; i++)
                {
                    Reg($"  [{i:X}] ");
                    Val($"0x{_vm.ram.GetByte(i):X2}\n", false);
                }
            }

            SendMessage(_regBox.Handle, WM_SETREDRAW, true, 0);
            _regBox.Invalidate();
        }

        private void SectionHeader(string text)
        {
            int s = _regBox.TextLength;
            _regBox.AppendText(text + "\n");
            _regBox.Select(s, text.Length);
            _regBox.SelectionColor = Color.FromArgb(0xAA, 0xAA, 0xAA);
            _regBox.Select(_regBox.TextLength, 0);
        }

        private void RegRow(string n1, string v1, bool c1, string n2, string v2, bool c2)
        {
            Reg($"  {n1,-4}"); Val($"{v1,-8}", c1);
            Reg($"  {n2,-4}"); Val($"{v2}\n", c2);
        }

        private void WideReg(string name, string value, bool changed)
        {
            Reg($"  {name,-4}"); Val($"{value}\n", changed);
        }

        private void Reg(string t)
        {
            int s = _regBox.TextLength;
            _regBox.AppendText(t);
            _regBox.Select(s, t.Length);
            _regBox.SelectionColor = CRegName;
            _regBox.Select(_regBox.TextLength, 0);
        }

        private void Val(string t, bool changed)
        {
            int s = _regBox.TextLength;
            _regBox.AppendText(t);
            _regBox.Select(s, t.Length);
            _regBox.SelectionColor = changed ? CRegChanged : CRegVal;
            _regBox.Select(_regBox.TextLength, 0);
        }

        private void Dim(string t)
        {
            int s = _regBox.TextLength;
            _regBox.AppendText(t);
            _regBox.Select(s, t.Length);
            _regBox.SelectionColor = Color.FromArgb(0x60, 0x60, 0x60);
            _regBox.Select(_regBox.TextLength, 0);
        }

        // ── Memory / code view ─────────────────────────────────────────────────

        private void UpdateMemory()
        {
            SendMessage(_memBox.Handle, WM_SETREDRAW, false, 0);

            _memBox.Clear();

            int codeEnd = _vm.ram.RAMLimit;
            int ip      = _vm.IP;

            // Header
            int hs = _memBox.TextLength;
            _memBox.AppendText("  ADDR   00  01  02  03  04  05   INSTRUCTION\n");
            _memBox.Select(hs, _memBox.TextLength - hs);
            _memBox.SelectionColor = Color.FromArgb(0x60, 0x60, 0x60);
            _memBox.Select(_memBox.TextLength, 0);

            int ipCharStart = -1;

            for (int i = 0; i + 5 < codeEnd; i += 6)
            {
                bool isCurrent = (i == ip);

                // Mark where IP row starts (for scroll)
                if (isCurrent) ipCharStart = _memBox.TextLength;

                // Address
                int rowStart = _memBox.TextLength;
                string addrStr = $"  0x{i:X4}  ";
                _memBox.AppendText(addrStr);
                _memBox.Select(rowStart, addrStr.Length);
                _memBox.SelectionColor = isCurrent ? CIpRowFg : CAddr;
                if (isCurrent) _memBox.SelectionBackColor = CIpRow;

                // 6 hex bytes
                var sb = new StringBuilder();
                for (int b = 0; b < 6; b++)
                    sb.Append($"{_vm.ram.memory[i + b]:X2}  ");
                string hexStr = sb.ToString();
                int hexStart = _memBox.TextLength;
                _memBox.AppendText(hexStr);
                _memBox.Select(hexStart, hexStr.Length);
                _memBox.SelectionColor = isCurrent ? CIpRowFg : CByte;
                if (isCurrent) _memBox.SelectionBackColor = CIpRow;

                // Decoded mnemonic
                string decoded = DecodeInstruction(i);
                int decStart = _memBox.TextLength;
                _memBox.AppendText(decoded);
                _memBox.Select(decStart, decoded.Length);
                _memBox.SelectionColor = isCurrent ? CIpRowFg : CDefault;
                if (isCurrent) _memBox.SelectionBackColor = CIpRow;

                int nlStart = _memBox.TextLength;
                _memBox.AppendText("\n");
                if (isCurrent)
                {
                    _memBox.Select(nlStart, 1);
                    _memBox.SelectionBackColor = CIpRow;
                }

                // Reset background
                _memBox.Select(_memBox.TextLength, 0);
                _memBox.SelectionBackColor = CBg;
            }

            // Scroll to IP row — must be the last thing set on SelectionStart
            // before redraw is re-enabled below, or whatever runs after this
            // silently overrides it before the control ever gets to show it.
            if (ipCharStart >= 0)
            {
                _memBox.SelectionStart = ipCharStart;
                _memBox.ScrollToCaret();
            }

            SendMessage(_memBox.Handle, WM_SETREDRAW, true, 0);
            _memBox.Invalidate();
        }

        private string DecodeInstruction(int offset)
        {
            byte b0 = _vm.ram.memory[offset];
            if (b0 == 0) return "(end)";

            byte opcode = (byte)((b0 >> 2) & 0x3F);
            byte mode   = (byte)(b0 & 0x03);
            byte p1b    = _vm.ram.memory[offset + 1];
            byte p2b0   = _vm.ram.memory[offset + 2];
            int  p2     = _vm.ram.memory[offset + 2]
                        | (_vm.ram.memory[offset + 3] << 8)
                        | (_vm.ram.memory[offset + 4] << 16)
                        | (_vm.ram.memory[offset + 5] << 24);

            string name = Decompiler.Instruction.GetName(opcode);
            if (string.IsNullOrEmpty(name)) return $"??? (0x{opcode:X2})";

            return name + " " + FormatOperands(opcode, mode, p1b, p2b0, p2);
        }

        private static string FormatOperands(byte op, byte mode, byte p1b, byte p2b0, int p2)
        {
            string R(byte b)  => Decompiler.Registers.GetName(b);
            string Rx(byte b) => Decompiler.Registers.IsRegister(b) ? R(b) : $"0x{b:X2}";

            return op switch
            {
                0x12 => "",                                                   // RET
                0x08 or 0x09 or 0x0D or 0x21 => R(p1b),                     // INC DEC NOT POP
                0x2A or 0x2B => $"0x{p1b:X2}",                              // SWI KEI
                0x02 or 0x1A or 0x1B or 0x1C or 0x1D => $"{R(p1b)}, {R(p2b0)}", // SWP TEQ TNE TLT TMT
                0x06 or 0x07 or 0x0E or 0x0F => $"{R(p1b)}, {p2}",          // SHL SHR ROL ROR
                0x3A => $"{Rx(p1b)}, 0x{p2:X4}",                             // MOM
                0x3B => $"{R(p1b)}, 0x{p2:X4}",                              // MOE
                0x20 or 0x10 or 0x11 or 0x13 or 0x14 or 0x17 or 0x18 =>    // PSH JMP CLL JMT JMF CLT CLF
                    (mode == 0 || mode == 2) ? R(p1b) : $"0x{(byte)p2:X2}",
                0x22 or 0x23 =>                                             // PSHN POPN — full 32-bit count, not truncated to a byte
                    (mode == 0 || mode == 2) ? R(p1b) : $"0x{p2:X}",
                _ =>
                    mode == 0 ? $"{R(p1b)}, {R(p2b0)}"                      // RegReg
                               : $"{R(p1b)}, 0x{p2:X2}",                    // RegVal / ValVal
            };
        }

        private void UpdateStatus()
        {
            if (IsHalted) return;
            string decoded = DecodeInstruction(_vm.IP);
            _statusInstr.Text = $"  ▶  0x{_vm.IP:X2}  {decoded}";
            SetStatusState("● Running", CSuccess);
        }

        private void SetStatusState(string text, Color colour)
        {
            _statusState.Text      = text;
            _statusState.ForeColor = colour;
        }

        // ── Output helper ──────────────────────────────────────────────────────

        private void AppendOutput(string text, Color colour)
        {
            int s = _outBox.TextLength;
            _outBox.AppendText(text);
            _outBox.Select(s, text.Length);
            _outBox.SelectionColor = colour;
            _outBox.Select(_outBox.TextLength, 0);
            _outBox.ScrollToCaret();
        }

        // ── UI construction ────────────────────────────────────────────────────

        private void BuildUI()
        {
            Text            = "Caca Studio – Debugger";
            Size            = new Size(1020, 680);
            MinimumSize     = new Size(720, 520);
            StartPosition   = FormStartPosition.CenterParent;
            BackColor       = CSide;
            ForeColor       = CDefault;
            Font            = new Font("Segoe UI", 9f);

            BuildPanels();
            BuildStatusBar();
            BuildToolbar();
        }

        private void BuildToolbar()
        {
            var bar = new ToolStrip
            {
                Renderer  = new DarkRenderer(),
                BackColor = CToolbar,
                GripStyle = ToolStripGripStyle.Hidden,
                Padding   = new Padding(4, 2, 0, 2),
            };

            _stepBtn  = DebugBtn("Step  F10",   "Execute one instruction (F10)", () => DoStep());
            _runBtn   = DebugBtn("▶ Run  F5",   "Run continuously (F5)",         () => StartRun());
            _pauseBtn = DebugBtn("⏸ Pause",     "Pause continuous run",          () => PauseRun());
            _stopBtn  = DebugBtn("■ Stop",      "Stop and reset VM",             () => StopAndReset());

            _pauseBtn.Enabled = false;

            bar.Items.Add(_stepBtn);
            bar.Items.Add(_runBtn);
            bar.Items.Add(_pauseBtn);
            bar.Items.Add(_stopBtn);
            bar.Items.Add(new ToolStripSeparator());

            // Speed label
            var speedLbl = new ToolStripLabel("Speed:")
            { ForeColor = Color.FromArgb(0xAA, 0xAA, 0xAA), Padding = new Padding(6, 0, 2, 0) };
            bar.Items.Add(speedLbl);

            // Speed trackbar embedded in toolstrip
            _speedBar = new TrackBar
            {
                Minimum      = 1, Maximum = 5, Value = 3,
                TickFrequency = 1,
                Width        = 100,
                Height       = 22,
                BackColor    = CToolbar,
            };
            var speedHost = new ToolStripControlHost(_speedBar) { AutoSize = false, Width = 110 };
            bar.Items.Add(speedHost);

            // Apply live, not just at the moment Run is clicked — dragging
            // the slider mid-run previously did nothing until Stop/Start
            // again, since _runTimer.Interval was only ever read once, in
            // StartRun().
            _speedBar.ValueChanged += (_, _) =>
            {
                if (_runTimer.Enabled)
                {
                    _runTimer.Interval = Delays[Math.Clamp(_speedBar.Value, 1, 5)];
                }
            };

            bar.Items.Add(new ToolStripSeparator());
            var stepsLbl = new ToolStripLabel("Steps: 0")
            { ForeColor = Color.FromArgb(0xAA, 0xAA, 0xAA) };
            bar.Items.Add(stepsLbl);

            _runTimer = new System.Windows.Forms.Timer();
            _runTimer.Tick += (_, _) =>
            {
                if (IsHalted) { _runTimer.Stop(); OnHalted(); return; }
                DoStep();
            };

            bar.Items.Add(new ToolStripSeparator());
            var resetBtn = DebugBtn("↺ Reset", "Reset VM to start", () => { StopAndReset(); });
            bar.Items.Add(resetBtn);

            _stepsLabelHost = stepsLbl;

            Controls.Add(bar);
        }

        // Hack-free field to hold the toolbar steps label
        private ToolStripLabel _stepsLabelHost = null!;

        private ToolStripButton DebugBtn(string text, string tip, Action action)
        {
            var btn = new ToolStripButton(text)
            {
                ToolTipText  = tip,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ForeColor    = Color.FromArgb(0xCC, 0xCC, 0xCC),
                BackColor    = CToolbar,
                Padding      = new Padding(6, 1, 6, 1),
                Font         = new Font("Segoe UI", 9f),
                AutoSize     = true,
            };
            btn.Click += (_, _) => action();
            return btn;
        }

        private void BuildPanels()
        {
            // Simple panel layout - no SplitContainers!
            // Left panel: Registers (fixed width)
            var leftPanel = new Panel
            {
                Dock      = DockStyle.Left,
                Width     = 240,
                BackColor = CSide,
            };

            _regBox = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = CSide,
                ForeColor   = CDefault,
                Font        = PickMonoFont(9.5f),
                ReadOnly    = true,
                BorderStyle = BorderStyle.None,
                WordWrap    = false,
                ScrollBars  = RichTextBoxScrollBars.Vertical,
                DetectUrls  = false,
            };
            leftPanel.Controls.Add(_regBox);
            leftPanel.Controls.Add(PanelLabel("  REGISTERS + STACK"));

            // Right panel: Memory (top) and Output (bottom)
            var rightPanel = new Panel
            {
                Dock      = DockStyle.Fill,
                BackColor = CBg,
            };

            // Output at bottom (fixed height)
            var outputContainer = new Panel
            {
                Dock      = DockStyle.Bottom,
                Height    = 150,
                BackColor = Color.FromArgb(0x12, 0x12, 0x12),
            };

            _outBox = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = Color.FromArgb(0x12, 0x12, 0x12),
                ForeColor   = CDefault,
                Font        = PickMonoFont(9f),
                ReadOnly    = true,
                BorderStyle = BorderStyle.None,
                WordWrap    = false,
                ScrollBars  = RichTextBoxScrollBars.Both,
                DetectUrls  = false,
            };
            outputContainer.Controls.Add(_outBox);
            outputContainer.Controls.Add(PanelLabel("  VM OUTPUT"));

            // Memory fills remaining space
            _memBox = new RichTextBox
            {
                Dock        = DockStyle.Fill,
                BackColor   = CBg,
                ForeColor   = CDefault,
                Font        = PickMonoFont(9.5f),
                ReadOnly    = true,
                BorderStyle = BorderStyle.None,
                WordWrap    = false,
                ScrollBars  = RichTextBoxScrollBars.Both,
                DetectUrls  = false,
            };
            rightPanel.Controls.Add(_memBox);
            rightPanel.Controls.Add(PanelLabel("  MEMORY  (6 bytes / instruction)"));
            rightPanel.Controls.Add(outputContainer);

            Controls.Add(rightPanel);
            Controls.Add(leftPanel);
        }

        private static Panel PanelLabel(string text)
        {
            return new Panel
            {
                Dock      = DockStyle.Top,
                Height    = 22,
                BackColor = Color.FromArgb(0x2D, 0x2D, 0x30),
                Controls  =
                {
                    new Label
                    {
                        Text      = text,
                        Dock      = DockStyle.Fill,
                        ForeColor = Color.FromArgb(0xAA, 0xAA, 0xAA),
                        BackColor = Color.Transparent,
                        Font      = new Font("Segoe UI", 8.5f),
                        TextAlign = ContentAlignment.MiddleLeft,
                        Padding   = new Padding(2, 0, 0, 0),
                    }
                }
            };
        }

        private void BuildStatusBar()
        {
            var bar = new StatusStrip
            {
                BackColor  = CToolbar,
                ForeColor  = CDefault,
                SizingGrip = false,
                Renderer   = new DarkRenderer(),
            };

            _statusState = new ToolStripStatusLabel("● Ready")
            { ForeColor = CSuccess };
            _statusInstr = new ToolStripStatusLabel("")
            { ForeColor = Color.FromArgb(0xCC, 0xCC, 0xCC), Spring = true, TextAlign = ContentAlignment.MiddleLeft };

            bar.Items.Add(_statusState);
            bar.Items.Add(new ToolStripSeparator());
            bar.Items.Add(_statusInstr);
            Controls.Add(bar);
        }

        // ── Run / pause / stop ────────────────────────────────────────────────

        private static readonly int[] Delays = { 0, 600, 150, 30, 8, 1 };

        private void StartRun()
        {
            if (IsHalted) return;
            _stepBtn.Enabled  = false;
            _runBtn.Enabled   = false;
            _pauseBtn.Enabled = true;
            _runTimer.Interval = Delays[Math.Clamp(_speedBar.Value, 1, 5)];
            _runTimer.Start();
            SetStatusState("● Running", CSuccess);
        }

        private void PauseRun()
        {
            _runTimer.Stop();
            _stepBtn.Enabled  = true;
            _runBtn.Enabled   = true;
            _pauseBtn.Enabled = false;
            SetStatusState("⏸ Paused", CInfo);
            UpdateDisplay();
        }

        private void StopAndReset()
        {
            _runTimer.Stop();
            InitVM();
            _stepBtn.Enabled  = true;
            _runBtn.Enabled   = true;
            _pauseBtn.Enabled = false;
            SetStatusState("● Ready", CSuccess);
            _outBox.Clear();
            UpdateDisplay();
        }

        // ── Keyboard shortcuts ────────────────────────────────────────────────

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F10) { DoStep();    return true; }
            if (keyData == Keys.F5)  { StartRun();  return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _runTimer?.Stop();
            // Restore a null console so the main form's next run doesn't crash
            Globals.console = new Caca.VM.Handlers.NullConsole();
            base.OnFormClosing(e);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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
    }
}
