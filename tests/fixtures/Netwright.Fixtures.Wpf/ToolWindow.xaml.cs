using System.Windows;

namespace Netwright.Fixtures.Wpf;

public partial class ToolWindow : Window
{
    public ToolWindow()
    {
        InitializeComponent();
    }

    private void BtnToolClose_Click(object sender, RoutedEventArgs e) => Close();
}
