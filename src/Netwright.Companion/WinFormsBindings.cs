using System.Windows.Forms;

namespace Netwright.Companion;

/// <summary>
/// WinForms bindings default to <see cref="DataSourceUpdateMode.OnValidation"/>, which writes to the
/// data source only when focus leaves the control. Text set through UI Automation never moves focus.
/// </summary>
internal static class WinFormsBindings
{
    /// <summary>Returns null when <paramref name="element"/> is not a WinForms control.</summary>
    public static int? Commit(nint element)
    {
        if (element == 0 || Control.FromHandle(element) is not { } control)
        {
            return null;
        }

        return control.Invoke(() => CommitControl(control));
    }

    public static int CommitControl(Control control)
    {
        var count = 0;
        foreach (Binding binding in control.DataBindings)
        {
            if (binding.DataSourceUpdateMode == DataSourceUpdateMode.OnValidation)
            {
                binding.WriteValue();
                count++;
            }
        }

        return count;
    }
}
