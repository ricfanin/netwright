using System.Globalization;
using System.Windows.Forms;

namespace Netwright.Companion;

/// <summary>
/// Runs the WinForms equivalent of UI Automation patterns from inside the Target App, on the control
/// that owns the element's window handle. Cross-process pattern calls let the app take the
/// foreground; in-process calls do not (ADR 0007).
/// </summary>
internal static class WinFormsActions
{
    /// <summary>Returns null when <paramref name="element"/> is not a WinForms control.</summary>
    public static string? Perform(string action, nint element, string? argument)
    {
        if (element == 0 || Control.FromHandle(element) is not { } control)
        {
            return null;
        }

        return control.Invoke(() => Run(control, action, argument));
    }

    private static string Run(Control control, string action, string? argument)
    {
        switch (action)
        {
            case "invoke" when control is IButtonControl button:
                button.PerformClick();
                return "ok";
            case "toggle" when control is CheckBox box:
                box.CheckState = box.CheckState switch
                {
                    CheckState.Unchecked => CheckState.Checked,
                    CheckState.Checked when box.ThreeState => CheckState.Indeterminate,
                    _ => CheckState.Unchecked,
                };
                return "ok";
            case "select" when control is RadioButton radio:
                radio.Checked = true;
                return "ok";
            case "setvalue" when control is TextBoxBase or ComboBox { DropDownStyle: not ComboBoxStyle.DropDownList }:
                control.Text = argument ?? "";
                return "ok " + WinFormsBindings.CommitControl(control).ToString(CultureInfo.InvariantCulture);

            // WinForms exposes TrackBar and NumericUpDown through the Value pattern, as text.
            case "setvalue" when control is TrackBar or NumericUpDown
                && double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var typed):
                return SetRange(control, typed);
            case "setrange" when double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var number):
                return SetRange(control, number);
            case "expand" when control is ComboBox combo:
                combo.DroppedDown = true;
                return "ok";
            case "collapse" when control is ComboBox combo:
                combo.DroppedDown = false;
                return "ok";
            case "selectitem" when control is ComboBox combo:
                return Select(combo.FindStringExact(argument ?? ""), index => combo.SelectedIndex = index);
            case "selectitem" when control is ListBox list:
                return Select(list.FindStringExact(argument ?? ""), index => list.SetSelected(index, true));
            default:
                return "unsupported";
        }
    }

    private static string SetRange(Control control, double number)
    {
        switch (control)
        {
            case TrackBar track:
                track.Value = (int)Math.Round(number);
                return "ok";
            case NumericUpDown numeric:
                numeric.Value = (decimal)number;
                return "ok";
            default:
                return "unsupported";
        }
    }

    private static string Select(int index, Action<int> select)
    {
        if (index < 0)
        {
            return "notfound";
        }

        select(index);
        return "ok";
    }
}
