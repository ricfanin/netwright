using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Application = System.Windows.Application;

namespace Netwright.Companion;

/// <summary>
/// Runs UI Automation patterns on WPF elements from inside the Target App. The same provider calls
/// made cross-process let the app take the foreground; made in-process they do not (ADR 0007).
/// </summary>
internal static class WpfActions
{
    /// <summary>
    /// Returns null when the runtime id is not a WPF element of this process, so another UI stack or
    /// UI Automation can handle it.
    /// </summary>
    public static string? Perform(string action, string runtimeId, string? argument)
    {
        // WPF builds the UI Automation runtime id of every peer as [7, process id, peer hash code].
        var parts = runtimeId.Split('.');
        if (parts.Length != 3 || parts[0] != "7"
            || parts[1] != Environment.ProcessId.ToString(CultureInfo.InvariantCulture)
            || !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hash)
            || Application.Current is not { } app)
        {
            return null;
        }

        return app.Dispatcher.Invoke(() =>
        {
            foreach (Window window in app.Windows)
            {
                if (Find(window, hash) is { } peer)
                {
                    return Run(peer, action, argument, window);
                }
            }

            return "notfound";
        });
    }

    private static string Run(AutomationPeer peer, string action, string? argument, Window window)
    {
        switch (action)
        {
            case "invoke" when peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke:
                invoke.Invoke();
                return "ok";
            case "toggle" when peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggle:
                toggle.Toggle();
                return "ok";
            case "select" when peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider item:
                item.Select();
                return "ok";
            case "deselect" when peer.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider item:
                item.RemoveFromSelection();
                return "ok";
            case "expand" when peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expand:
                expand.Expand();
                return "ok";
            case "collapse" when peer.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider collapse:
                collapse.Collapse();
                return "ok";
            case "setvalue" when peer.GetPattern(PatternInterface.Value) is IValueProvider value:
                value.SetValue(argument ?? "");
                return "ok " + WpfBindings.CommitTree(window).ToString(CultureInfo.InvariantCulture);
            case "setrange" when peer.GetPattern(PatternInterface.RangeValue) is IRangeValueProvider range
                && double.TryParse(argument, NumberStyles.Float, CultureInfo.InvariantCulture, out var number):
                range.SetValue(number);
                return "ok";
            case "selectitem":
                return SelectItem(peer, argument ?? "");
            default:
                return "unsupported";
        }
    }

    private static string SelectItem(AutomationPeer owner, string name)
    {
        // A closed ComboBox may expose no item peers; selecting through the Selector needs no drop-down.
        if (owner is UIElementAutomationPeer { Owner: Selector selector })
        {
            foreach (var candidate in selector.Items)
            {
                if (ItemText(selector, candidate) == name)
                {
                    selector.SelectedItem = candidate;
                    return "ok";
                }
            }
        }

        foreach (var child in owner.GetChildren() ?? [])
        {
            if (child.GetName() != name)
            {
                continue;
            }

            if (child.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider item)
            {
                item.Select();
                return "ok";
            }

            if (child.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoke)
            {
                invoke.Invoke();
                return "ok";
            }
        }

        return "notfound";
    }

    /// <summary>The text UI Automation shows for an item: its container's automation name, else the item itself.</summary>
    private static string? ItemText(ItemsControl owner, object? item)
    {
        if (owner.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement container
            && AutomationProperties.GetName(container) is { Length: > 0 } name)
        {
            return name;
        }

        return item is FrameworkElement element && AutomationProperties.GetName(element) is { Length: > 0 } own
            ? own
            : item?.ToString();
    }

    /// <summary>
    /// Looks first at peers already attached to visual elements (cheap, covers controls), then walks
    /// the automation tree, where item peers live.
    /// </summary>
    private static AutomationPeer? Find(Window window, int hash)
    {
        var visuals = new Stack<DependencyObject>();
        visuals.Push(window);
        while (visuals.Count > 0)
        {
            var current = visuals.Pop();
            if (current is UIElement element && UIElementAutomationPeer.FromElement(element) is { } attached && RuntimeHelpers.GetHashCode(attached) == hash)
            {
                return attached;
            }

            if (current is Visual)
            {
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                {
                    visuals.Push(VisualTreeHelper.GetChild(current, i));
                }
            }
        }

        var peers = new Stack<AutomationPeer>();
        peers.Push(UIElementAutomationPeer.CreatePeerForElement(window));
        while (peers.Count > 0)
        {
            var peer = peers.Pop();
            if (RuntimeHelpers.GetHashCode(peer) == hash)
            {
                return peer;
            }

            foreach (var child in peer.GetChildren() ?? [])
            {
                peers.Push(child);
            }
        }

        return null;
    }
}
