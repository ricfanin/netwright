using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Netwright.Fixtures.WinUI;

/// <summary>
/// The "Confirm" dialog as a real top-level window owned by the main window (a ContentDialog would not be a
/// separate UIA window). Modality is enforced like WPF ShowDialog: the owner is disabled while it is open.
/// </summary>
public sealed partial class ConfirmWindow : Window
{
    private readonly Window _owner;

    public ConfirmWindow(Window owner)
    {
        InitializeComponent();

        _owner = owner;
        NativeWindow.SetOwner(this, owner);

        var presenter = OverlappedPresenter.CreateForDialog();
        AppWindow.SetPresenter(presenter);
        try
        {
            // Requires the owner to be set first.
            presenter.IsModal = true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"fixture: OverlappedPresenter.IsModal unavailable: {ex.Message}");
        }

        AppWindow.IsShownInSwitchers = false;
        NativeWindow.ResizeDips(this, 300, 150);
        NativeWindow.CenterOnOwner(this, owner);

        // Re-enable the owner before this window is destroyed so activation returns to it.
        Closed += (_, _) => NativeWindow.SetEnabled(_owner, true);
    }

    /// <summary>Disables the owner and shows the dialog; returns immediately (the result arrives through Closed).</summary>
    public void ShowModal()
    {
        NativeWindow.SetEnabled(_owner, false);
        Activate();
    }

    /// <summary>"Yes" or "No" when closed through a button; null when closed any other way.</summary>
    public string? Choice { get; private set; }

    private void BtnYes_Click(object sender, RoutedEventArgs e)
    {
        Choice = "Yes";
        Close();
    }

    private void BtnNo_Click(object sender, RoutedEventArgs e)
    {
        Choice = "No";
        Close();
    }
}
