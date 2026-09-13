using System.Runtime.InteropServices;
using Interop.UIAutomationClient;
using Netwright.Engine.Model;

namespace Netwright.Engine.Uia;

/// <summary>Live calls into the Target App through UI Automation control patterns.</summary>
internal static class PatternCalls
{
    private const int UiaElementNotEnabled = unchecked((int)0x80040200);
    private const int UiaElementNotAvailable = unchecked((int)0x80040201);
    private const int UiaNoClickablePoint = unchecked((int)0x80040202);
    private const int UiaNotSupported = unchecked((int)0x80040204);
    private const int UiaTimeout = unchecked((int)0x80131505);
    private const int UiaInvalidOperation = unchecked((int)0x80131509);

    public static T Get<T>(UiNode node, int patternId, string patternName)
        where T : class
    {
        var native = node.Native ?? throw new NetwrightException(ErrorCodes.StaleRef, $"{Describe(node)} is no longer available.");
        object? pattern;
        try
        {
            pattern = native.GetCurrentPattern(patternId);
        }
        catch (COMException ex)
        {
            throw Translate(ex, node);
        }

        return pattern as T ?? throw new NetwrightException(
            ErrorCodes.NotSupported,
            $"{Describe(node)} does not support the {patternName} pattern.",
            "desktop_inspect lists the patterns this element supports.");
    }

    public static void Invoke(UiNode node) => Call(node, () => Get<IUIAutomationInvokePattern>(node, PatternIds.Invoke, "Invoke").Invoke());

    public static void Toggle(UiNode node) => Call(node, () => Get<IUIAutomationTogglePattern>(node, PatternIds.Toggle, "Toggle").Toggle());

    public static ToggleState CurrentToggleState(UiNode node) =>
        Call(node, () => Get<IUIAutomationTogglePattern>(node, PatternIds.Toggle, "Toggle").CurrentToggleState);

    public static void Select(UiNode node) => Call(node, () => Get<IUIAutomationSelectionItemPattern>(node, PatternIds.SelectionItem, "SelectionItem").Select());

    public static void RemoveFromSelection(UiNode node) =>
        Call(node, () => Get<IUIAutomationSelectionItemPattern>(node, PatternIds.SelectionItem, "SelectionItem").RemoveFromSelection());

    public static void Expand(UiNode node) => Call(node, () => Get<IUIAutomationExpandCollapsePattern>(node, PatternIds.ExpandCollapse, "ExpandCollapse").Expand());

    public static void Collapse(UiNode node) => Call(node, () => Get<IUIAutomationExpandCollapsePattern>(node, PatternIds.ExpandCollapse, "ExpandCollapse").Collapse());

    public static ExpandCollapseState CurrentExpandState(UiNode node) =>
        Call(node, () => Get<IUIAutomationExpandCollapsePattern>(node, PatternIds.ExpandCollapse, "ExpandCollapse").CurrentExpandCollapseState);

    public static void SetValue(UiNode node, string value) => Call(node, () => Get<IUIAutomationValuePattern>(node, PatternIds.Value, "Value").SetValue(value));

    public static void SetRangeValue(UiNode node, double value) =>
        Call(node, () => Get<IUIAutomationRangeValuePattern>(node, PatternIds.RangeValue, "RangeValue").SetValue(value));

    public static (double Minimum, double Maximum)? RangeLimits(UiNode node)
    {
        try
        {
            var pattern = Get<IUIAutomationRangeValuePattern>(node, PatternIds.RangeValue, "RangeValue");
            return (pattern.CurrentMinimum, pattern.CurrentMaximum);
        }
        catch (Exception ex) when (ex is NetwrightException or COMException)
        {
            return null;
        }
    }

    public static void ScrollIntoView(UiNode node) =>
        Call(node, () => Get<IUIAutomationScrollItemPattern>(node, PatternIds.ScrollItem, "ScrollItem").ScrollIntoView());

    public static double Scroll(UiNode node, ScrollAmount horizontal, ScrollAmount vertical) => Call(node, () =>
    {
        var pattern = Get<IUIAutomationScrollPattern>(node, PatternIds.Scroll, "Scroll");
        pattern.Scroll(horizontal, vertical);
        return pattern.CurrentVerticalScrollPercent;
    });

    public static double SetScrollPercent(UiNode node, double horizontal, double vertical) => Call(node, () =>
    {
        var pattern = Get<IUIAutomationScrollPattern>(node, PatternIds.Scroll, "Scroll");
        pattern.SetScrollPercent(horizontal, vertical);
        return pattern.CurrentVerticalScrollPercent;
    });

    public static void SetWindowState(UiNode node, WindowVisualState state) =>
        Call(node, () => Get<IUIAutomationWindowPattern>(node, PatternIds.Window, "Window").SetWindowVisualState(state));

    public static void CloseWindow(UiNode node) => Call(node, () => Get<IUIAutomationWindowPattern>(node, PatternIds.Window, "Window").Close());

    public static string DefaultAction(UiNode node) =>
        Call(node, () => Get<IUIAutomationLegacyIAccessiblePattern>(node, PatternIds.LegacyIAccessible, "LegacyIAccessible").CurrentDefaultAction ?? "");

    public static void DoDefaultAction(UiNode node) =>
        Call(node, () => Get<IUIAutomationLegacyIAccessiblePattern>(node, PatternIds.LegacyIAccessible, "LegacyIAccessible").DoDefaultAction());

    public static void SetFocus(UiNode node) => Call(node, () => node.Native!.SetFocus());

    public static IReadOnlyList<string> SupportedPatternNames(UiNode node)
    {
        var names = new List<string>();
        foreach (var pattern in Enum.GetValues<UiPatterns>())
        {
            if (pattern != UiPatterns.None && node.Has(pattern))
            {
                names.Add(pattern.ToString());
            }
        }

        return names;
    }

    public static string Describe(UiNode node) =>
        node.Name.Length > 0 ? $"{node.Role} \"{node.Name}\"" : node.AutomationId.Length > 0 ? $"{node.Role} #{node.AutomationId}" : node.Role;

    public static NetwrightException Translate(COMException ex, UiNode node) => ex.HResult switch
    {
        UiaElementNotAvailable => new NetwrightException(ErrorCodes.StaleRef, $"{Describe(node)} disappeared while acting on it.", "Take a new desktop_snapshot.", ex),
        UiaElementNotEnabled => new NetwrightException(ErrorCodes.NotActionable, $"{Describe(node)} is disabled.", "Wait until it is enabled (desktop_wait state=enabled).", ex),
        UiaNotSupported => new NetwrightException(ErrorCodes.NotSupported, $"{Describe(node)} does not support this operation.", "desktop_inspect lists the supported patterns.", ex),
        UiaNoClickablePoint => new NetwrightException(ErrorCodes.NotActionable, $"{Describe(node)} has no clickable point.", "Scroll it into view first.", ex),
        UiaInvalidOperation => new NetwrightException(ErrorCodes.NotSupported, $"The Target App rejected the operation on {Describe(node)}: {ex.Message}", null, ex),
        UiaTimeout => new NetwrightException(ErrorCodes.AppNotResponding, "The Target App did not respond in time.", "It may be busy or hung; wait and retry.", ex),
        _ => new NetwrightException(ErrorCodes.NotSupported, $"UI Automation failed on {Describe(node)}: {ex.Message}", null, ex),
    };

    private static void Call(UiNode node, Action action)
    {
        try
        {
            action();
        }
        catch (COMException ex)
        {
            throw Translate(ex, node);
        }
    }

    private static T Call<T>(UiNode node, Func<T> func)
    {
        try
        {
            return func();
        }
        catch (COMException ex)
        {
            throw Translate(ex, node);
        }
    }
}
