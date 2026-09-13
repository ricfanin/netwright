using Microsoft.UI.Xaml;

namespace Netwright.Fixtures.WinUI;

/// <summary>Non-modal window owned by the main window.</summary>
public sealed partial class ToolWindow : Window
{
    public ToolWindow(Window owner)
    {
        InitializeComponent();

        NativeWindow.SetOwner(this, owner);
        NativeWindow.ResizeDips(this, 360, 180);
        NativeWindow.CenterOnOwner(this, owner);
    }

    private void BtnToolClose_Click(object sender, RoutedEventArgs e) => Close();
}
