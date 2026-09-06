using System;
using System.Windows.Forms;

namespace Caca.VM.Studio
{
    /// <summary>Entry point for the Caca Studio WinForms IDE.</summary>
    static class Program
    {
        /// <summary>
        /// Application entry point. Enables visual styles and launches the <see cref="MainForm"/>.
        /// Must run on a single-threaded apartment (STA) thread as required by WinForms.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (var splash = new SplashForm())
            {
                splash.Show();
                while (!splash.IsFinished)
                {
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(15);
                }
            }

            Application.Run(new MainForm());
        }
    }
}
