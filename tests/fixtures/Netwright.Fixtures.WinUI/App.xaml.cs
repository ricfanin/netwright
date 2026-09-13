using Microsoft.UI.Xaml;

namespace Netwright.Fixtures.WinUI;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();

        // btnCrash must terminate the process: the exception is reported but never marked handled.
        UnhandledException += (_, e) =>
        {
            Console.Error.WriteLine($"fixture-unhandled: {e.Exception}");
            Console.Error.Flush();
            e.Handled = false;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string? initialTab = null;
        var noActivate = false;

        // Unpackaged apps: LaunchActivatedEventArgs.Arguments is not reliable, read the process command line.
        var commandLine = Environment.GetCommandLineArgs();
        for (var i = 1; i < commandLine.Length; i++)
        {
            var arg = commandLine[i];
            if (string.Equals(arg, "--no-activate", StringComparison.OrdinalIgnoreCase))
            {
                noActivate = true;
            }
            else if (string.Equals(arg, "--tab", StringComparison.OrdinalIgnoreCase) && i + 1 < commandLine.Length)
            {
                initialTab = commandLine[++i];
            }
        }

        _window = new MainWindow();
        if (initialTab is not null)
        {
            _window.SelectTab(initialTab);
        }

        if (noActivate)
        {
            _window.AppWindow.Show(activateWindow: false);
        }
        else
        {
            _window.Activate();
        }
    }
}
