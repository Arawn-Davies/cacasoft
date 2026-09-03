using Avalonia;
using System;

namespace AIL_Studio_Avalonia
{
    internal static class Program
    {
        /// <summary>
        /// Application entry point.
        /// [STAThread] isn't required by Avalonia, but is harmless to keep for parity
        /// with the WinForms host's threading expectations.
        /// </summary>
        [STAThread]
        public static void Main(string[] args) =>
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
    }
}
