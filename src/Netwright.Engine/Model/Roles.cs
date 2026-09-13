using Netwright.Engine.Uia;

namespace Netwright.Engine.Model;

/// <summary>
/// Maps UI Automation control types to the short role names used in Snapshots and Selectors.
/// </summary>
public static class Roles
{
    private static readonly Dictionary<int, string> ByControlType = new()
    {
        [ControlTypeIds.Button] = "button",
        [ControlTypeIds.Calendar] = "calendar",
        [ControlTypeIds.CheckBox] = "checkbox",
        [ControlTypeIds.ComboBox] = "combobox",
        [ControlTypeIds.Edit] = "textbox",
        [ControlTypeIds.Hyperlink] = "link",
        [ControlTypeIds.Image] = "image",
        [ControlTypeIds.ListItem] = "item",
        [ControlTypeIds.List] = "list",
        [ControlTypeIds.Menu] = "menu",
        [ControlTypeIds.MenuBar] = "menubar",
        [ControlTypeIds.MenuItem] = "menuitem",
        [ControlTypeIds.ProgressBar] = "progressbar",
        [ControlTypeIds.RadioButton] = "radio",
        [ControlTypeIds.ScrollBar] = "scrollbar",
        [ControlTypeIds.Slider] = "slider",
        [ControlTypeIds.Spinner] = "spinner",
        [ControlTypeIds.StatusBar] = "statusbar",
        [ControlTypeIds.Tab] = "tabs",
        [ControlTypeIds.TabItem] = "tab",
        [ControlTypeIds.Text] = "text",
        [ControlTypeIds.ToolBar] = "toolbar",
        [ControlTypeIds.ToolTip] = "tooltip",
        [ControlTypeIds.Tree] = "tree",
        [ControlTypeIds.TreeItem] = "treeitem",
        [ControlTypeIds.Custom] = "custom",
        [ControlTypeIds.Group] = "group",
        [ControlTypeIds.Thumb] = "thumb",
        [ControlTypeIds.DataGrid] = "grid",
        [ControlTypeIds.DataItem] = "row",
        [ControlTypeIds.Document] = "document",
        [ControlTypeIds.SplitButton] = "splitbutton",
        [ControlTypeIds.Window] = "window",
        [ControlTypeIds.Pane] = "pane",
        [ControlTypeIds.Header] = "header",
        [ControlTypeIds.HeaderItem] = "columnheader",
        [ControlTypeIds.Table] = "table",
        [ControlTypeIds.TitleBar] = "titlebar",
        [ControlTypeIds.Separator] = "separator",
        [ControlTypeIds.SemanticZoom] = "semanticzoom",
        [ControlTypeIds.AppBar] = "appbar",
    };

    /// <summary>Alternative names Agents commonly use, mapped to canonical roles.</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["edit"] = "textbox",
        ["input"] = "textbox",
        ["listitem"] = "item",
        ["radiobutton"] = "radio",
        ["dataitem"] = "row",
        ["datagrid"] = "grid",
        ["tabitem"] = "tab",
        ["tabcontrol"] = "tabs",
        ["hyperlink"] = "link",
        ["label"] = "text",
        ["dialog"] = "window",
        ["headeritem"] = "columnheader",
        ["dropdown"] = "combobox",
    };

    private static readonly HashSet<string> Known = [.. ByControlType.Values];

    public static string FromControlType(int controlTypeId) =>
        ByControlType.TryGetValue(controlTypeId, out var role) ? role : "element";

    /// <summary>Returns the canonical role for a name typed by the Agent, or null if unknown.</summary>
    public static string? Normalize(string role)
    {
        if (Aliases.TryGetValue(role, out var canonical))
        {
            return canonical;
        }

        var lower = role.ToLowerInvariant();
        return Known.Contains(lower) || lower == "element" ? lower : null;
    }

    public static IEnumerable<string> All => Known.Order(StringComparer.Ordinal);
}
