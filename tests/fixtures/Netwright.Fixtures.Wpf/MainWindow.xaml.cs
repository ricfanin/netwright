using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Netwright.Fixtures.Wpf;

public partial class MainWindow : Window
{
    private static readonly string[] Countries = ["Italy", "Germany", "France", "Spain", "United States"];

    private ToolWindow? _toolWindow;

    public MainWindow()
    {
        InitializeComponent();

        cmbCountry.ItemsSource = Countries;
        cmbCountry.SelectedIndex = -1;

        gridOrders.ItemsSource = Enumerable.Range(1, 1000)
            .Select(i => new Order(i, $"Customer {i}", (i * 10).ToString("0.00", CultureInfo.InvariantCulture)))
            .ToList();

        for (var i = 1; i <= 100; i++)
        {
            var number = i;
            var button = new Button { Content = $"Item {number}" };
            AutomationProperties.SetAutomationId(button, $"btnItem{number:000}");
            button.Click += (_, _) => lblScrollClicked.Text = $"Clicked Item {number}";
            pnlScrollItems.Children.Add(button);
        }

        // handledEventsToo: TextBox marks navigation/editing keys (arrows, Backspace, ...) as handled.
        txtKeys.AddHandler(KeyDownEvent, new KeyEventHandler(TxtKeys_KeyDown), handledEventsToo: true);
    }

    public void SelectTab(string header)
    {
        foreach (var item in tabs.Items.OfType<TabItem>())
        {
            if (string.Equals(item.Header as string, header, StringComparison.OrdinalIgnoreCase))
            {
                tabs.SelectedItem = item;
                return;
            }
        }
    }

    // ---- Top bar ----

    private void OpenModal_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ConfirmDialog { Owner = this };
        dialog.ShowDialog();
        if (dialog.Choice is not null)
        {
            lblModalResult.Text = $"Modal result: {dialog.Choice}";
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnOpenWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_toolWindow is not null)
        {
            _toolWindow.Activate();
            return;
        }

        _toolWindow = new ToolWindow { Owner = this };
        _toolWindow.Closed += (_, _) => _toolWindow = null;
        _toolWindow.Show();
    }

    private void BtnTrace_Click(object sender, RoutedEventArgs e)
    {
        Trace.WriteLine("fixture-trace: hello");
        Console.Out.WriteLine("fixture-stdout: hello");
        Console.Out.Flush();
        Console.Error.WriteLine("fixture-stderr: hello");
        Console.Error.Flush();
        lblDiag.Text = "Diagnostics written";
    }

    private void BtnCrash_Click(object sender, RoutedEventArgs e) =>
        throw new InvalidOperationException("Fixture crash requested");

    // ---- Form ----

    private void ChkTerms_Changed(object sender, RoutedEventArgs e) =>
        btnSubmit.IsEnabled = chkTerms.IsChecked == true;

    private void BtnSubmit_Click(object sender, RoutedEventArgs e)
    {
        var country = cmbCountry.SelectedItem as string ?? string.Empty;
        var plan = rbPlanPro.IsChecked == true ? "Pro" : "Free";
        var quantity = (int)Math.Round(sldQuantity.Value);
        lblResult.Text = $"Submitted: {txtName.Text} <{txtEmail.Text}> {country} {plan} x{quantity}";
    }

    private void BtnReset_Click(object sender, RoutedEventArgs e)
    {
        txtName.Text = string.Empty;
        txtEmail.Text = string.Empty;
        cmbCountry.SelectedIndex = -1;
        chkTerms.IsChecked = false;
        rbPlanFree.IsChecked = true;
        sldQuantity.Value = 1;
        lblResult.Text = string.Empty;
    }

    // ---- Async ----

    private async void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        btnLoad.IsEnabled = false;
        lblStatus.Text = "Loading...";

        await Task.Delay(1500);

        lstItems.Items.Clear();
        lstItems.Items.Add("Alpha");
        lstItems.Items.Add("Beta");
        lstItems.Items.Add("Gamma");
        lblStatus.Text = "Loaded 3 items";
        btnLoad.IsEnabled = true;
    }

    private async void BtnEnableLater_Click(object sender, RoutedEventArgs e)
    {
        await Task.Delay(1000);
        btnLater.IsEnabled = true;
    }

    private void BtnLater_Click(object sender, RoutedEventArgs e) => lblLater.Text = "Later clicked";

    // ---- Tree ----

    private void TreeFolders_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem item)
        {
            lblSelectedNode.Text = $"Selected: {item.Header}";
        }
    }

    // ---- Keyboard ----

    private void TxtKeys_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };
        lblLastKey.Text = $"Last key: {key}";
    }

    private void PnlMouse_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        lblMouse.Text = e.ClickCount == 2 ? "Mouse: double" : "Mouse: left";
    }

    private void PnlMouse_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        lblMouse.Text = "Mouse: right";
    }

    private sealed record Order(int Id, string Customer, string Amount);
}
