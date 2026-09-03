using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Artemis_IL.Handlers;
using AIL_Studio_Avalonia.Views;

namespace AIL_Studio_Avalonia
{
    /// <summary>
    /// Bridges Artemis-VM I/O to the IDE's output panel. Ported from AIL-Studio/StudioConsole.cs.
    /// VM execution runs on a background thread (see MainWindow.RunAsync), so every write
    /// marshals to the UI thread, and every read blocks the calling (background) thread on
    /// a modal dialog shown on the UI thread — matching the WinForms version's
    /// <c>RichTextBox.Invoke</c> semantics via <see cref="Dispatcher"/> instead.
    /// </summary>
    internal sealed class AvaloniaStudioConsole : VConsole
    {
        private readonly Action<string, IBrush?> _append;
        private readonly Window _owner;

        public AvaloniaStudioConsole(Action<string, IBrush?> append, Window owner)
        {
            _append = append ?? throw new ArgumentNullException(nameof(append));
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public override void Write(string text) => _append(text, null);

        public override void Write(char ch) => _append(ch.ToString(), null);

        public override void WriteLine(string text) => _append(text + "\n", null);

        public override byte Read()
        {
            string result = ShowInputDialogBlocking("VM is waiting for a single character:");
            return result.Length > 0 ? (byte)result[0] : (byte)0;
        }

        public override string ReadLine() =>
            ShowInputDialogBlocking("VM is waiting for input:");

        private string ShowInputDialogBlocking(string prompt)
        {
            var tcs = new TaskCompletionSource<string>();
            Dispatcher.UIThread.Post(async () =>
            {
                var dlg = new InputDialog(prompt);
                string? result = await dlg.ShowDialog<string?>(_owner);
                tcs.SetResult(result ?? string.Empty);
            });
            // Blocks this (background) thread only — never called on the UI thread, since
            // Read()/ReadLine() are only reachable from VM execution running via Task.Run.
            return tcs.Task.GetAwaiter().GetResult();
        }
    }
}
