using System.Text;
using System.Text.Json;
using Artemis_IL;
// AIL_Studio.Compiler.BuildException, aliased: this project's own root namespace
// (AIL_Studio.Maui) nests under AIL_Studio, the same parent as AIL_Studio.Compiler.
// "Compiler" itself resolves to the namespace regardless of a using-alias for it
// (confirmed empirically — global::-qualified below instead), but BuildException
// has no such collision, so the alias works fine for it.
using BuildException = AIL_Studio.Compiler.BuildException;

namespace AIL_Studio.Maui;

/// <summary>
/// Main IDE page. MVP scope — editor (Monaco via HybridWebView), compile, run, clean
/// console output. Ported from AIL-Studio/MainForm.cs by way of the removed
/// AIL-Studio.Avalonia attempt; the step debugger and full file-save UX stay out of
/// scope for this pass (see SaveAs below for the one deliberate simplification).
/// </summary>
public partial class MainPage : ContentPage
{
    private static readonly Color COutputInfo = Color.FromArgb("#9CDCFE");
    private static readonly Color COutputSuccess = Color.FromArgb("#6A9955");
    private static readonly Color COutputError = Color.FromArgb("#F44747");

    private string? _filePath;
    private string? _displayName;
    private bool _modified;

    public MainPage()
    {
        InitializeComponent();
        OutputLabel.FormattedText = new FormattedString();
    }

    // ── HybridWebView bridge ────────────────────────────────────────────────

    private void OnEditorRawMessageReceived(object? sender, HybridWebViewRawMessageReceivedEventArgs e)
    {
        string message = e.Message ?? string.Empty;
        if (message == "modified")
        {
            _modified = true;
            UpdateTitle();
        }
        else if (message.StartsWith("cursor:"))
        {
            try
            {
                var pos = JsonSerializer.Deserialize<CursorPosition>(message.AsSpan(7));
                if (pos != null) StatusPosLabel.Text = $"Ln {pos.line}, Col {pos.column}";
            }
            catch (JsonException) { /* ignore malformed position payloads */ }
        }
    }

    private sealed class CursorPosition
    {
        public int line { get; set; }
        public int column { get; set; }
    }

    // Text crosses the C#<->JS boundary as base64, not as an embedded JS string
    // literal. Embedding real source text (with newlines, quotes, etc.) via
    // JsonSerializer.Serialize + string interpolation silently failed for every
    // real-content case tested, including as little as 50 characters, while
    // trivial no-escape-sequence literals always worked — base64's alphabet
    // ([A-Za-z0-9+/=]) has nothing in it any layer of this pipeline can
    // misinterpret, which sidesteps the problem entirely regardless of its
    // exact root cause.
    private async Task<string> GetEditorTextAsync()
    {
        string? b64 = await Editor.EvaluateJavaScriptAsync("window.getEditorTextBase64()");
        if (string.IsNullOrEmpty(b64) || b64 == "null") return string.Empty;
        // No JsonSerializer.Deserialize here: base64 text has no quote/backslash
        // characters to need JSON-unescaping, so the JS side returns it raw (no
        // JSON.stringify) — just strip any stray wrapping quotes EvaluateJavaScriptAsync
        // itself might add for a string result, whichever convention it follows.
        string trimmed = b64.Trim('"');
        return Encoding.UTF8.GetString(Convert.FromBase64String(trimmed));
    }

    private Task SetEditorTextAsync(string text)
    {
        // No C#-side readiness gating: index.html's setEditorText is defined
        // synchronously at page load and queues internally (window._pendingText)
        // until Monaco actually initializes, so this is always safe to call.
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        string js = $"window.setEditorTextBase64({JsonSerializer.Serialize(base64)})";
        return Editor.EvaluateJavaScriptAsync(js);
    }

    // ── File menu ────────────────────────────────────────────────────────────

    private async void OnNewClicked(object? sender, EventArgs e)
    {
        if (!await ConfirmDiscardIfModifiedAsync()) return;
        await SetEditorTextAsync(string.Empty);
        _filePath = null;
        _displayName = null;
        _modified = false;
        UpdateTitle();
    }

    private async void OnOpenClicked(object? sender, EventArgs e)
    {
        if (!await ConfirmDiscardIfModifiedAsync()) return;
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = "Open AIL Source" });
            if (result is null) return;

            string text = await File.ReadAllTextAsync(result.FullPath);
            await SetEditorTextAsync(text);
            _filePath = result.FullPath;
            _displayName = null;
            _modified = false;
            UpdateTitle();
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Open failed", ex.Message, "OK");
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_filePath is null) { OnSaveAsClicked(sender, e); return; }
        await File.WriteAllTextAsync(_filePath, await GetEditorTextAsync());
        _modified = false;
        UpdateTitle();
    }

    private async void OnSaveAsClicked(object? sender, EventArgs e)
    {
        // MAUI has no built-in cross-platform save-file dialog (unlike Avalonia's
        // StorageProvider). Simplified for this MVP pass: prompt for a filename and
        // write into AppDataDirectory rather than an arbitrary user-chosen folder.
        string? name = await DisplayPromptAsync("Save As", "File name:",
            initialValue: _filePath is null ? "untitled.ail" : Path.GetFileName(_filePath));
        if (string.IsNullOrWhiteSpace(name)) return;

        string path = Path.Combine(FileSystem.Current.AppDataDirectory, name);
        await File.WriteAllTextAsync(path, await GetEditorTextAsync());
        _filePath = path;
        _displayName = null;
        _modified = false;
        UpdateTitle();
        await DisplayAlertAsync("Saved", $"Saved to:\n{path}", "OK");
    }

    private async void OnLoadExampleHelloWorldClicked(object? sender, EventArgs e) =>
        await LoadExampleAsync("hello_world_db.ail");

    private async void OnLoadExampleCalculatorClicked(object? sender, EventArgs e) =>
        await LoadExampleAsync("calculator.ail");

    private async Task LoadExampleAsync(string fileName)
    {
        if (!await ConfirmDiscardIfModifiedAsync()) return;

        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync($"examples/{fileName}");
            using var reader = new StreamReader(stream);
            string text = await reader.ReadToEndAsync();

            await SetEditorTextAsync(text);
            _filePath = null;
            _displayName = fileName;
            _modified = false;
            UpdateTitle();
        }
        catch (Exception ex)
        {
            // async void event handlers swallow exceptions silently otherwise —
            // surface this rather than have "Load Example" fail with zero symptom.
            await DisplayAlertAsync("Load Example failed", $"{ex.GetType().Name}: {ex.Message}", "OK");
        }
    }

    // ── Build menu ───────────────────────────────────────────────────────────

    private async void OnCompileClicked(object? sender, EventArgs e) => await CompileAsync();

    private async void OnCompileAndRunClicked(object? sender, EventArgs e)
    {
        byte[]? bytecode = await CompileAsync();
        if (bytecode is null) return;

        AppendOutput("── VM output ──\n", COutputInfo);
        RunAsync(bytecode);
    }

    private async Task<byte[]?> CompileAsync()
    {
        ClearOutput();
        AppendOutput("── Compiling… ──\n", COutputInfo);
        try
        {
            string source = await GetEditorTextAsync();
            var compiler = new global::AIL_Studio.Compiler.Compiler(source);
            byte[] bytecode = compiler.Compile();
            AppendOutput(
                $"✓ Compiled — {bytecode.Length / 6} instruction(s), {bytecode.Length} byte(s).\n",
                COutputSuccess);
            return bytecode;
        }
        catch (BuildException ex)
        {
            AppendOutput($"✗ Error on line {ex.SrcLineNumber}: {ex.Message}\n", COutputError);
            return null;
        }
        catch (Exception ex)
        {
            AppendOutput($"✗ {ex.Message}\n", COutputError);
            return null;
        }
    }

    private void RunAsync(byte[] code)
    {
        Globals.console = new MauiStudioConsole(AppendOutput, this);
        Globals.DebugMode = false;

        Task.Run(() =>
        {
            try
            {
                Executable.Run(code);
                AppendOutput("\n── Done ─────────────────────────────\n", COutputInfo);
            }
            catch (Exception ex)
            {
                AppendOutput($"\n✗ Runtime error: {ex.Message}\n", COutputError);
            }
        });
    }

    // ── Help menu ────────────────────────────────────────────────────────────

    private async void OnAboutClicked(object? sender, EventArgs e) =>
        await DisplayAlertAsync("About AIL Studio",
            "AIL Studio (MAUI)\n\nCross-platform IDE for the Artemis Intermediate Language.",
            "OK");

    // ── Output panel ─────────────────────────────────────────────────────────

    private void ClearOutput()
    {
        MainThread.BeginInvokeOnMainThread(() => OutputLabel.FormattedText = new FormattedString());
    }

    internal void AppendOutput(string text, Color? color)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            OutputLabel.FormattedText ??= new FormattedString();
            OutputLabel.FormattedText.Spans.Add(new Span
            {
                Text = text,
                TextColor = color ?? Color.FromArgb("#D4D4D4")
            });
            await Task.Delay(1); // let layout catch up before scrolling
            await OutputScroll.ScrollToAsync(OutputLabel, ScrollToPosition.End, false);
        });
    }

    // ── Title / status ───────────────────────────────────────────────────────

    private void UpdateTitle()
    {
        string name = _filePath is not null ? Path.GetFileName(_filePath)
            : _displayName is not null ? _displayName
            : "Untitled";
        Title = $"{(_modified ? "● " : string.Empty)}{name} — AIL Studio";
        StatusFileLabel.Text = _filePath ?? _displayName ?? "New file";
    }

    private async Task<bool> ConfirmDiscardIfModifiedAsync()
    {
        if (!_modified) return true;
        return await DisplayAlertAsync("Discard changes?", "You have unsaved changes. Discard them?", "Discard", "Cancel");
    }
}
