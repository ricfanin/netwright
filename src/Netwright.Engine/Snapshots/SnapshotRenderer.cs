using System.Globalization;
using System.Text;
using Netwright.Engine.Model;
using Netwright.Engine.Refs;
using Netwright.Engine.Uia;

namespace Netwright.Engine.Snapshots;

public sealed record SnapshotOptions
{
    /// <summary>Render every element instead of the filtered view.</summary>
    public bool Full { get; init; }

    public bool IncludeOffscreen { get; init; }

    public int MaxLines { get; init; } = 300;

    /// <summary>Items shown per list, grid or tree before the rest is summarized.</summary>
    public int MaxItemsPerContainer { get; init; } = 20;
}

public sealed record RenderResult(string Text, int Lines, int OmittedElements);

/// <summary>
/// Turns captured elements into the compact text the Agent reads. The default view keeps what the
/// Agent needs to act and to understand the screen: interactive elements, text, and named
/// containers. Unnamed layout panes are collapsed, scrollbars dropped, and long collections cut.
/// </summary>
public sealed class SnapshotRenderer(RefRegistry refs, SnapshotOptions options)
{
    private const int MaxNameLength = 80;
    private const int MaxValueLength = 60;

    public SnapshotOptions Options => options;

    public RenderResult Render(IEnumerable<UiNode> roots)
    {
        var state = new RenderState { MaxLines = options.MaxLines };
        foreach (var root in roots)
        {
            RenderNode(root, 0, parentName: null, state, prefix: "- ");
        }

        if (state.Omitted > 0)
        {
            state.Builder.Append(CultureInfo.InvariantCulture,
                $"... {state.Omitted} more elements not shown. Narrow with desktop_snapshot root=<ref>, or target elements with a Selector.");
            state.Builder.AppendLine();
        }

        return new RenderResult(state.Builder.ToString().TrimEnd(), state.Lines, state.Omitted);
    }

    /// <summary>Renders a subtree with a custom line marker (used by Change Reports).</summary>
    internal int RenderInto(StringBuilder builder, UiNode root, string marker, int indent, int maxLines, string? parentName)
    {
        var state = new RenderState { Builder = builder, MaxLines = maxLines };
        RenderNode(root, indent, parentName, state, marker);
        return state.Lines;
    }

    /// <summary>
    /// True when the node would appear as its own line in the filtered view. Change Reports only
    /// mention such nodes.
    /// </summary>
    public bool IsReportable(UiNode node)
    {
        if (IsSkippedRole(node) || IsCollapsedContainer(node))
        {
            return false;
        }

        if (node.ControlTypeId == ControlTypeIds.Text)
        {
            return node.Name.Length > 0 && !string.Equals(node.Name, node.Parent?.Name, StringComparison.OrdinalIgnoreCase) && !IsGridContainer(node.Parent);
        }

        if (node.ControlTypeId == ControlTypeIds.Image && node.Name.Length == 0)
        {
            return false;
        }

        // Cells are summarized into their row, column headers into one line.
        return node.Parent is null || (!IsGridRow(node.Parent) && !IsHeaderRow(node.Parent));
    }

    /// <param name="node">The element.</param>
    /// <param name="withRef">Include the Ref (text elements never get one).</param>
    /// <param name="withAttributes">Include states and values; without them the line only identifies the element.</param>
    /// <param name="withFocus">Include the <c>focused</c> attribute.</param>
    public string RenderLine(UiNode node, bool withRef = true, bool withAttributes = true, bool withFocus = true)
    {
        var sb = new StringBuilder();
        AppendLineBody(sb, node, withRef && ShouldHaveRef(node), attributes: withAttributes, focus: withFocus);
        return sb.ToString();
    }

    private void RenderNode(UiNode node, int depth, string? parentName, RenderState state, string prefix)
    {
        if (state.Lines >= state.MaxLines)
        {
            state.Omitted += CountVisible(node);
            return;
        }

        if (!options.Full)
        {
            if (IsSkippedRole(node))
            {
                return;
            }

            if (node.ControlTypeId == ControlTypeIds.Text)
            {
                // Cells of a partially visible row end up directly under the grid when the row itself is filtered out as offscreen.
                if (node.Name.Length > 0 && !string.Equals(node.Name, parentName, StringComparison.OrdinalIgnoreCase) && !IsGridContainer(node.Parent))
                {
                    WriteLine(node, depth, state, prefix);
                }

                return;
            }

            if (node.ControlTypeId == ControlTypeIds.Image && node.Name.Length == 0)
            {
                return;
            }

            if (IsHeaderRow(node))
            {
                var columns = node.Children.Select(c => c.Name).Where(n => n.Length > 0).ToList();
                if (columns.Count > 0)
                {
                    Indent(state.Builder, depth);
                    state.Builder.Append(prefix).Append("columns: ").AppendJoin(" | ", columns.Select(c => Truncate(c, 40))).AppendLine();
                    state.Lines++;
                }

                return;
            }

            if (IsGridRow(node))
            {
                WriteLine(node, depth, state, prefix, RowSummary(node), roleOverride: "row");
                return;
            }

            if (IsCollapsedContainer(node))
            {
                RenderChildren(node, depth, parentName, state, prefix);
                return;
            }
        }

        WriteLine(node, depth, state, prefix);
        if (!options.Full)
        {
            // The parts of composite controls (slider thumbs, spinner buttons, combo box edit and
            // drop-down button) are implementation detail; the control's own line already has its state.
            if (node.ControlTypeId is ControlTypeIds.Slider or ControlTypeIds.Spinner or ControlTypeIds.ProgressBar or ControlTypeIds.ScrollBar)
            {
                return;
            }

            if (node.ControlTypeId == ControlTypeIds.ComboBox)
            {
                foreach (var item in node.Descendants().Where(d => d.ControlTypeId == ControlTypeIds.ListItem && (options.IncludeOffscreen || !d.IsOffscreen)).Take(options.MaxItemsPerContainer))
                {
                    RenderNode(item, depth + 1, node.Name, state, prefix);
                }

                return;
            }
        }

        RenderChildren(node, depth + 1, node.Name, state, prefix);
    }

    private void RenderChildren(UiNode node, int depth, string? parentName, RenderState state, string prefix)
    {
        var shownItems = 0;
        var hiddenItems = 0;
        var offscreen = 0;
        var childPrefix = prefix;

        foreach (var child in node.Children)
        {
            if (!options.IncludeOffscreen && child.IsOffscreen)
            {
                if (!IsSkippedRole(child))
                {
                    offscreen++;
                }

                continue;
            }

            if (!options.Full && (IsItem(child) || IsGridRow(child)))
            {
                if (shownItems >= options.MaxItemsPerContainer)
                {
                    hiddenItems++;
                    continue;
                }

                shownItems++;
            }

            RenderNode(child, depth, parentName, state, childPrefix);
        }

        if (hiddenItems > 0 || offscreen > 0)
        {
            var parts = new List<string>();
            if (hiddenItems > 0)
            {
                parts.Add($"{hiddenItems} more items");
            }

            if (offscreen > 0)
            {
                parts.Add($"{offscreen} offscreen");
            }

            Indent(state.Builder, depth);
            state.Builder.Append(childPrefix).Append("... ").AppendJoin(", ", parts).AppendLine();
            state.Lines++;
        }
    }

    private void WriteLine(UiNode node, int depth, RenderState state, string prefix, string? nameOverride = null, string? roleOverride = null)
    {
        Indent(state.Builder, depth);
        state.Builder.Append(prefix);
        AppendLineBody(state.Builder, node, ShouldHaveRef(node), nameOverride, roleOverride: roleOverride);
        state.Builder.AppendLine();
        state.Lines++;
    }

    private void AppendLineBody(StringBuilder sb, UiNode node, bool withRef, string? nameOverride = null, bool attributes = true, bool focus = true, string? roleOverride = null)
    {
        sb.Append(roleOverride ?? node.Role);

        var name = nameOverride ?? node.Name;
        if (name.Length > 0)
        {
            sb.Append(" \"").Append(Escape(Truncate(name, nameOverride is null ? MaxNameLength : 120))).Append('"');
        }

        if (withRef)
        {
            sb.Append(" [").Append(refs.GetOrAssign(node)).Append(']');
        }

        if (node.AutomationId.Length > 0 && !string.Equals(node.AutomationId, node.Name, StringComparison.Ordinal) &&
            node.ControlTypeId is not (ControlTypeIds.ListItem or ControlTypeIds.DataItem or ControlTypeIds.TreeItem))
        {
            sb.Append(" #").Append(node.AutomationId);
        }

        if (!attributes)
        {
            return;
        }

        foreach (var attribute in Attributes(node, focus))
        {
            sb.Append(' ').Append(attribute);
        }
    }

    private static IEnumerable<string> Attributes(UiNode node, bool focus)
    {
        if (!node.IsEnabled && node.ControlTypeId != ControlTypeIds.Text)
        {
            yield return "disabled";
        }

        if (focus && node.HasFocus && node.ControlTypeId != ControlTypeIds.Window)
        {
            yield return "focused";
        }

        if (node.IsModal == true)
        {
            yield return "modal";
        }

        if (node.IsMinimized || (node.ControlTypeId == ControlTypeIds.Window && node.IsOffscreen))
        {
            yield return "minimized";
        }

        if (node.Toggle is { } toggle && node.Has(UiPatterns.Toggle))
        {
            yield return toggle switch
            {
                ToggleValue.On => "checked",
                ToggleValue.Off => "unchecked",
                _ => "mixed",
            };
        }
        else if (node.ControlTypeId == ControlTypeIds.RadioButton)
        {
            yield return node.IsSelected == true ? "checked" : "unchecked";
        }
        else if (node.IsSelected == true)
        {
            yield return "selected";
        }

        if (node.Expand is ExpandValue.Expanded or ExpandValue.PartiallyExpanded)
        {
            yield return "expanded";
        }
        else if (node.Expand == ExpandValue.Collapsed)
        {
            yield return "collapsed";
        }

        if (node.RangeValue is { } range && node.Has(UiPatterns.RangeValue))
        {
            yield return node.RangeMinimum is { } minimum && node.RangeMaximum is { } maximum
                ? string.Create(CultureInfo.InvariantCulture, $"value={FormatNumber(range)} ({FormatNumber(minimum)}-{FormatNumber(maximum)})")
                : string.Create(CultureInfo.InvariantCulture, $"value={FormatNumber(range)}");
        }
        else if (node.Has(UiPatterns.Value) && node.ControlTypeId != ControlTypeIds.Text)
        {
            if (node.IsPassword)
            {
                yield return "value=***";
            }
            else if (!string.IsNullOrEmpty(node.Value) && !string.Equals(node.Value, node.Name, StringComparison.Ordinal))
            {
                yield return $"value=\"{Escape(Truncate(node.Value, MaxValueLength))}\"";
            }

            if (node.ValueIsReadOnly && node.ControlTypeId == ControlTypeIds.Edit)
            {
                yield return "readonly";
            }
        }

        if (node.GridRowCount is { } rows)
        {
            yield return string.Create(CultureInfo.InvariantCulture, $"rows={rows}");
        }

        if (node.IsScrollable && node.Name.Length == 0 && node.ControlTypeId is ControlTypeIds.Pane or ControlTypeIds.Group or ControlTypeIds.Custom)
        {
            yield return "scrollable";
        }
    }

    private static bool ShouldHaveRef(UiNode node) => node.ControlTypeId != ControlTypeIds.Text || node.Has(UiPatterns.Invoke);

    private bool IsCollapsedContainer(UiNode node) =>
        !options.Full &&
        node.Name.Length == 0 &&
        node.ControlTypeId is ControlTypeIds.Pane or ControlTypeIds.Group or ControlTypeIds.Custom or ControlTypeIds.Document &&
        !node.IsInteractive &&
        !node.IsScrollable;

    private bool IsSkippedRole(UiNode node) =>
        !options.Full && node.ControlTypeId is ControlTypeIds.ScrollBar or ControlTypeIds.Thumb or ControlTypeIds.Separator or ControlTypeIds.TitleBar;

    private static bool IsItem(UiNode node) =>
        node.ControlTypeId is ControlTypeIds.ListItem or ControlTypeIds.DataItem or ControlTypeIds.TreeItem;

    private static bool IsGridContainer(UiNode? node) => node?.ControlTypeId is ControlTypeIds.DataGrid or ControlTypeIds.Table;

    /// <summary>A column header row: WPF exposes a Header element; WinForms a row of HeaderItems.</summary>
    internal static bool IsHeaderRow(UiNode node) =>
        node.ControlTypeId == ControlTypeIds.Header ||
        (IsGridContainer(node.Parent) && node.Children.Count > 0 && node.Children.All(c => c.ControlTypeId is ControlTypeIds.HeaderItem or ControlTypeIds.Header));

    /// <summary>
    /// A data row. WPF DataGrid rows are DataItems holding Custom cells; WinForms DataGridView rows are
    /// Custom elements holding DataItem (or Edit) cells; ListView rows are DataItems in a List.
    /// </summary>
    internal static bool IsGridRow(UiNode node) =>
        !IsHeaderRow(node) &&
        ((IsGridContainer(node.Parent) && node.ControlTypeId is ControlTypeIds.DataItem or ControlTypeIds.Custom or ControlTypeIds.ListItem) ||
         (node.ControlTypeId == ControlTypeIds.DataItem && !IsGridContainer(node.Parent?.Parent)));

    private static string RowSummary(UiNode row)
    {
        var cells = row.Children
            .Where(c => c.ControlTypeId != ControlTypeIds.HeaderItem)
            .Select(CellText)
            .Where(t => t.Length > 0)
            .ToList();

        return cells.Count > 0 ? string.Join(" | ", cells) : row.Name;
    }

    private static string CellText(UiNode cell)
    {
        if (!string.IsNullOrEmpty(cell.Value))
        {
            return cell.Value!;
        }

        if (cell.Name.Length > 0)
        {
            return cell.Name;
        }

        return cell.Descendants().FirstOrDefault(d => d.Name.Length > 0)?.Name ?? "";
    }

    private int CountVisible(UiNode node) =>
        node.DescendantsAndSelf().Count(n => (options.IncludeOffscreen || !n.IsOffscreen) && IsReportable(n));

    private static void Indent(StringBuilder sb, int depth) => sb.Append(' ', depth * 2);

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : string.Concat(text.AsSpan(0, max - 3), "...");

    internal static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static string FormatNumber(double value) =>
        value.ToString(Math.Abs(value % 1) < 1e-9 ? "0" : "0.##", CultureInfo.InvariantCulture);

    private sealed class RenderState
    {
        public StringBuilder Builder { get; init; } = new();

        public int MaxLines { get; init; } = int.MaxValue;

        public int Lines { get; set; }

        public int Omitted { get; set; }
    }
}
