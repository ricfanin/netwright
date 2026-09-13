using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Netwright.Fixtures.WinForms
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            EnableDpiAwareness();

            // Unhandled UI-thread exceptions must terminate the process (btnCrash),
            // not show the WinForms "unhandled exception" dialog.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var options = FixtureOptions.Parse(args);
            Application.Run(new MainForm(options));
        }

        private static void EnableDpiAwareness()
        {
#if NET
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
#else
            try
            {
                SetProcessDPIAware();
            }
            catch (EntryPointNotFoundException)
            {
                // Very old Windows: stay DPI-unaware.
            }
#endif
        }

#if !NET
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
#endif
    }

    internal sealed class FixtureOptions
    {
        public string? InitialTab { get; private set; }

        public bool NoActivate { get; private set; }

        public static FixtureOptions Parse(string[] args)
        {
            var options = new FixtureOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (string.Equals(arg, "--no-activate", StringComparison.OrdinalIgnoreCase))
                {
                    options.NoActivate = true;
                }
                else if (string.Equals(arg, "--tab", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.InitialTab = args[++i];
                }
            }

            return options;
        }
    }
}
