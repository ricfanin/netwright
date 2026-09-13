using System.Windows;

namespace Netwright.Fixtures.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        string? initialTab = null;
        var noActivate = false;

        for (var i = 0; i < e.Args.Length; i++)
        {
            var arg = e.Args[i];
            if (string.Equals(arg, "--no-activate", StringComparison.OrdinalIgnoreCase))
            {
                noActivate = true;
            }
            else if (string.Equals(arg, "--tab", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
            {
                initialTab = e.Args[++i];
            }
        }

        var window = new MainWindow();
        if (initialTab is not null)
        {
            window.SelectTab(initialTab);
        }

        if (noActivate)
        {
            window.ShowActivated = false;
        }

        MainWindow = window;
        window.Show();
    }
}
