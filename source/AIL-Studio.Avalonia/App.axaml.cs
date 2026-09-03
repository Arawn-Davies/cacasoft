using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AIL_Studio_Avalonia.Views;
#if DEBUG
using Avalonia.Diagnostics;
#endif

namespace AIL_Studio_Avalonia
{
    public partial class App : Application
    {
        public override void Initialize() => AvaloniaXamlLoader.Load(this);

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.MainWindow = new MainWindow();

#if DEBUG
            // Temporary — diagnosing the blank-TextEditor rendering bug. F12 to open.
            this.AttachDevTools();
#endif

            base.OnFrameworkInitializationCompleted();
        }
    }
}
