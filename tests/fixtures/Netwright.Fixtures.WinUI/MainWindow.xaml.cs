using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Netwright.Fixtures.WinUI;

public sealed partial class MainWindow : Window
{
    private static readonly string[] Countries = ["Italy", "Germany", "France", "Spain", "United States"];

    private ToolWindow? _toolWindow;
    private ConfirmWindow? _confirmWindow;
    private long _lastLeftPressTicks;

    public MainWindow()
    {
        InitializeComponent();

        NativeWindow.ResizeDips(this, 1000, 750);
        NativeWindow.CenterOnScreen(this);
        Closed += (_, _) => Application.Current.Exit();

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

        // handledEventsToo: TextBox marks navigation/editing keys (arrows, Enter, Backspace, ...) as handled.
        txtKeys.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(TxtKeys_KeyDown), handledEventsToo: true);
    }

    public void SelectTab(string header)
    {
        foreach (var item in tabs.TabItems.OfType<TabViewItem>())
        {
            if (string.Equals(item.Header as string, header, StringComparison.OrdinalIgnoreCase))
            {
                tabs.SelectedItem = item;
                return;
            }
        }
    }

    // ---- Top bar ----

    // Deferred through the dispatcher so a UIA Invoke returns before the dialog opens (like WPF/WinForms).
    private void OpenModal_Click(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(OpenModal);

    private void OpenModal()
    {
        if (_confirmWindow is not null)
        {
            _confirmWindow.Activate();
            return;
        }

        var dialog = new ConfirmWindow(this);
        dialog.Closed += (_, _) =>
        {
            _confirmWindow = null;
            if (dialog.Choice is not null)
            {
                lblModalResult.Text = $"Modal result: {dialog.Choice}";
            }
        };
        _confirmWindow = dialog;
        dialog.ShowModal();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnOpenWindow_Click(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_toolWindow is not null)
            {
                _toolWindow.Activate();
                return;
            }

            _toolWindow = new ToolWindow(this);
            _toolWindow.Closed += (_, _) => _toolWindow = null;
            _toolWindow.Activate();
        });

    private void BtnTrace_Click(object sender, RoutedEventArgs e)
    {
        Trace.WriteLine("fixture-trace: hello");
        Console.Out.WriteLine("fixture-stdout: hello");
        Console.Out.Flush();
        Console.Error.WriteLine("fixture-stderr: hello");
        Console.Error.Flush();
        lblDiag.Text = "Diagnostics written";
    }

    // Thrown from a dispatcher callback on the UI thread, outside any UIA provider call, so nothing can swallow it.
    private void BtnCrash_Click(object sender, RoutedEventArgs e) =>
        DispatcherQueue.TryEnqueue(() => throw new InvalidOperationException("Fixture crash requested"));

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

    // ---- Grid ----

    // Each (recycled) row container is named "Id | Customer | Amount" so the row is identifiable through UIA.
    private void GridOrders_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is Order order)
        {
            AutomationProperties.SetName(args.ItemContainer, order.ToString());
        }
    }

    // ---- Tree ----

    // WinUI TreeView has no automation peer of its own (AutomationProperties on it only produce a peerless
    // "Group" container); the UIA Tree element is its inner TreeViewList, so the AutomationId and Name go there.
    private void TreeFolders_Loaded(object sender, RoutedEventArgs e)
    {
        if (FindDescendant<TreeViewList>(treeFolders) is { } list)
        {
            AutomationProperties.SetAutomationId(list, "treeFolders");
            AutomationProperties.SetName(list, "Folders");
        }
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private void TreeFolders_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        if (sender.SelectedNode?.Content is string text)
        {
            lblSelectedNode.Text = $"Selected: {text}";
        }
    }

    // ---- Keyboard ----

    private void TxtKeys_KeyDown(object sender, KeyRoutedEventArgs e) =>
        lblLastKey.Text = $"Last key: {e.Key}";

    private void PnlMouse_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var properties = e.GetCurrentPoint(pnlMouse).Properties;
        if (properties.IsRightButtonPressed)
        {
            lblMouse.Text = "Mouse: right";
            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        // Second left press within the system double-click time = double click (like WPF ClickCount == 2).
        var now = Environment.TickCount64;
        if (_lastLeftPressTicks != 0 && now - _lastLeftPressTicks <= NativeWindow.DoubleClickTime())
        {
            lblMouse.Text = "Mouse: double";
            _lastLeftPressTicks = 0;
        }
        else
        {
            lblMouse.Text = "Mouse: left";
            _lastLeftPressTicks = now;
        }
    }
}

public sealed class Order(int id, string customer, string amount)
{
    public int Id { get; } = id;

    public string IdText => Id.ToString(CultureInfo.InvariantCulture);

    public string Customer { get; } = customer;

    public string Amount { get; } = amount;

    public override string ToString() => $"{Id} | {Customer} | {Amount}";
}
