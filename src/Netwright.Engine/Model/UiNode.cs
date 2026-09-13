using System.Drawing;
using Interop.UIAutomationClient;

namespace Netwright.Engine.Model;

[Flags]
public enum UiPatterns
{
    None = 0,
    Invoke = 1 << 0,
    Value = 1 << 1,
    Toggle = 1 << 2,
    SelectionItem = 1 << 3,
    Selection = 1 << 4,
    ExpandCollapse = 1 << 5,
    RangeValue = 1 << 6,
    Scroll = 1 << 7,
    ScrollItem = 1 << 8,
    Grid = 1 << 9,
    Window = 1 << 10,
    LegacyIAccessible = 1 << 11,
}

public enum ToggleValue
{
    Off,
    On,
    Indeterminate,
}

public enum ExpandValue
{
    Collapsed,
    Expanded,
    PartiallyExpanded,
    LeafNode,
}

/// <summary>
/// One UI element captured from the Target App, with every property the Snapshot, Selectors,
/// Change Reports and actions need. Captured in a single cached UI Automation query, so reading
/// it never crosses the process boundary.
/// </summary>
public sealed class UiNode
{
    /// <summary>Stable identity of the element for as long as it exists (its UIA RuntimeId).</summary>
    public required string Key { get; init; }

    public required int ControlTypeId { get; init; }

    public required string Role { get; init; }

    public string Name { get; init; } = "";

    public string AutomationId { get; init; } = "";

    public string ClassName { get; init; } = "";

    public string FrameworkId { get; init; } = "";

    public int ProcessId { get; init; }

    public nint WindowHandle { get; init; }

    public Rectangle Bounds { get; init; }

    public bool IsEnabled { get; init; } = true;

    public bool IsOffscreen { get; init; }

    public bool HasFocus { get; init; }

    public bool IsFocusable { get; init; }

    public bool IsPassword { get; init; }

    public UiPatterns Patterns { get; init; }

    public string? Value { get; init; }

    public bool ValueIsReadOnly { get; init; }

    public ToggleValue? Toggle { get; init; }

    public bool? IsSelected { get; init; }

    public ExpandValue? Expand { get; init; }

    public double? RangeValue { get; init; }

    public double? RangeMinimum { get; init; }

    public double? RangeMaximum { get; init; }

    public bool IsScrollable { get; init; }

    public int? GridRowCount { get; init; }

    public bool? IsModal { get; init; }

    /// <summary>True for a top-level window that is minimized.</summary>
    public bool IsMinimized { get; init; }

    public UiNode? Parent { get; internal set; }

    public List<UiNode> Children { get; } = [];

    /// <summary>The live UIA element, used only when an action needs to call into the Target App.</summary>
    internal IUIAutomationElement? Native { get; init; }

    public bool Has(UiPatterns pattern) => (Patterns & pattern) == pattern;

    /// <summary>True when the element accepts direct interaction, as opposed to layout or text.</summary>
    public bool IsInteractive =>
        Has(UiPatterns.Invoke) || Has(UiPatterns.Value) || Has(UiPatterns.Toggle) || Has(UiPatterns.SelectionItem) ||
        Has(UiPatterns.ExpandCollapse) || Has(UiPatterns.RangeValue) ||
        ControlTypeId is Uia.ControlTypeIds.Button or Uia.ControlTypeIds.Edit or Uia.ControlTypeIds.CheckBox or
            Uia.ControlTypeIds.RadioButton or Uia.ControlTypeIds.ComboBox or Uia.ControlTypeIds.MenuItem or
            Uia.ControlTypeIds.Hyperlink or Uia.ControlTypeIds.Slider or Uia.ControlTypeIds.TabItem or
            Uia.ControlTypeIds.ListItem or Uia.ControlTypeIds.TreeItem or Uia.ControlTypeIds.DataItem;

    /// <summary>
    /// The nearest window containing this node (or the node itself). Owned windows such as dialogs
    /// appear inside their owner in UI Automation, so this is not always the top of the tree.
    /// </summary>
    public UiNode Window
    {
        get
        {
            for (var node = this; node is not null; node = node.Parent)
            {
                if (node.ControlTypeId == Uia.ControlTypeIds.Window)
                {
                    return node;
                }
            }

            var root = this;
            while (root.Parent is not null)
            {
                root = root.Parent;
            }

            return root;
        }
    }

    public IEnumerable<UiNode> Ancestors()
    {
        for (var node = Parent; node is not null; node = node.Parent)
        {
            yield return node;
        }
    }

    public IEnumerable<UiNode> DescendantsAndSelf()
    {
        var stack = new Stack<UiNode>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }
    }

    public IEnumerable<UiNode> Descendants() => DescendantsAndSelf().Skip(1);

    /// <summary>The text an Expectation or Selector compares against: the value for inputs, otherwise the name.</summary>
    public string Text => !string.IsNullOrEmpty(Value) && ControlTypeId != Uia.ControlTypeIds.ComboBox ? Value! : Name;

    public override string ToString() => $"{Role} \"{Name}\" ({Key})";
}
