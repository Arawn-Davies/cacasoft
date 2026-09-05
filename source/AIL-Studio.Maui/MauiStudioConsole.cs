using Artemis_IL.Handlers;

namespace AIL_Studio.Maui;

/// <summary>
/// Bridges Artemis-VM I/O to the IDE's output panel. Ported from
/// AIL-Studio.Avalonia/AvaloniaStudioConsole.cs (itself ported from
/// AIL-Studio/StudioConsole.cs). VM execution runs on a background thread (see
/// MainPage.RunAsync), so every write marshals to the UI thread via the page's own
/// AppendOutput (already MainThread-dispatched), and every read blocks the calling
/// background thread on a native DisplayPromptAsync dialog shown on the UI thread.
/// </summary>
internal sealed class MauiStudioConsole : VConsole
{
    private readonly Action<string, Color?> _append;
    private readonly Page _owner;

    public MauiStudioConsole(Action<string, Color?> append, Page owner)
    {
        _append = append ?? throw new ArgumentNullException(nameof(append));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public override void Write(string text) => _append(text, null);

    public override void Write(char ch) => _append(ch.ToString(), null);

    public override void WriteLine(string text) => _append(text + "\n", null);

    public override byte Read()
    {
        string result = ShowInputPromptBlocking("VM is waiting for a single character:");
        return result.Length > 0 ? (byte)result[0] : (byte)0;
    }

    public override string ReadLine() =>
        ShowInputPromptBlocking("VM is waiting for input:");

    private string ShowInputPromptBlocking(string prompt)
    {
        var tcs = new TaskCompletionSource<string>();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            string? result = await _owner.DisplayPromptAsync("AIL Studio", prompt);
            tcs.SetResult(result ?? string.Empty);
        });
        // Blocks this (background) thread only — never called on the UI thread, since
        // Read()/ReadLine() are only reachable from VM execution running via Task.Run.
        return tcs.Task.GetAwaiter().GetResult();
    }
}
