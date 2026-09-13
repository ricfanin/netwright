using System.Drawing;
using Netwright.Engine.Model;
using Netwright.Engine.Uia;

namespace Netwright.Engine.Tests;

/// <summary>Builds in-memory UI trees without a Target App.</summary>
internal static class TestTree
{
    private static int _nextKey;

    public static UiNode Node(
        int controlType,
        string name = "",
        string id = "",
        UiPatterns patterns = UiPatterns.None,
        bool enabled = true,
        bool offscreen = false,
        bool focused = false,
        ToggleValue? toggle = null,
        bool? selected = null,
        ExpandValue? expand = null,
        string? value = null,
        int? rows = null,
        string? key = null,
        params UiNode[] children)
    {
        var node = new UiNode
        {
            Key = key ?? $"k{Interlocked.Increment(ref _nextKey)}",
            ControlTypeId = controlType,
            Role = Roles.FromControlType(controlType),
            Name = name,
            AutomationId = id,
            Patterns = patterns,
            IsEnabled = enabled,
            IsOffscreen = offscreen,
            HasFocus = focused,
            Toggle = toggle,
            IsSelected = selected,
            Expand = expand,
            Value = value,
            GridRowCount = rows,
            Bounds = new Rectangle(0, 0, 10, 10),
            WindowHandle = controlType == ControlTypeIds.Window ? 1 : 0,
        };

        foreach (var child in children)
        {
            child.Parent = node;
            node.Children.Add(child);
        }

        return node;
    }

    public static UiNode Window(string title, params UiNode[] children) =>
        Node(ControlTypeIds.Window, title, patterns: UiPatterns.Window, children: children);

    public static UiNode Pane(params UiNode[] children) => Node(ControlTypeIds.Pane, children: children);

    public static UiNode Button(string name, string id = "", bool enabled = true, string? key = null) =>
        Node(ControlTypeIds.Button, name, id, UiPatterns.Invoke, enabled: enabled, key: key);

    public static UiNode TextBox(string name, string id = "", string? value = null, string? key = null, bool focused = false) =>
        Node(ControlTypeIds.Edit, name, id, UiPatterns.Value, value: value, key: key, focused: focused);

    public static UiNode Text(string name, string id = "", string? key = null) => Node(ControlTypeIds.Text, name, id, key: key);

    public static UiTree Tree(params UiNode[] windows) => new(windows, DateTimeOffset.UtcNow);
}
