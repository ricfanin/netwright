using System.Windows;

namespace Netwright.Fixtures.Wpf;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>"Yes" or "No" when closed through a button; null when closed any other way.</summary>
    public string? Choice { get; private set; }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        Choice = "Yes";
        DialogResult = true;
    }

    private void BtnNo_Click(object sender, RoutedEventArgs e)
    {
        Choice = "No";
        DialogResult = false;
    }
}
