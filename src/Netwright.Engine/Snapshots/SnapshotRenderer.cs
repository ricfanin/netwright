using System.Globalization;
using System.Text;
using Netwright.Engine.Model;
using Netwright.Engine.Refs;
using Netwright.Engine.Selectors;
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

    /// <summary>
    /// Levels rendered below the roots (0: the roots only). A node at the limit gets one line counting
    /// the elements below it, with its Ref, so the Agent can continue with <c>root=</c>. Null renders all levels.
    /// Applies to Snapshots only; Change Reports ignore it.
    /// </summary>
    public int? MaxDepth { get; init; }
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
        if (options.MaxDepth < 0)
        {
            throw NetwrightException.InvalidArgument("depth must be 0 or more.");
        }

        var state = new RenderState { MaxLines = options.MaxLines, MaxDepth = options.MaxDepth };
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

    /// <summary>
    /// Renders a subtree with a custom line marker (used by Change Reports). <see cref="SnapshotOptions.MaxDepth"/>
    /// does not apply: a Change Report shows the whole added subtree.
    /// </summary>
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

        // Cells are summarized into their row, column headers into one line. A cell that is a control
        // of its own keeps its line, like the other controls of a row.
        return node.Parent is not { } parent || (IsGridRow(parent) ? IsRowAction(node) : !IsHeaderRow(parent));
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
                    if (!state.Counting)
                    {
                        Indent(state.Builder, depth);
                        state.Builder.Append(prefix).Append("columns: ").AppendJoin(" | ", columns.Select(c => Truncate(c, 40))).AppendLine();
                    }

                    state.Lines++;
                }

                return;
            }

            if (IsGridRow(node))
            {
                WriteLine(node, depth, state, prefix, RowSummary(node), roleOverride: "row");
                RenderRowActions(node, depth + 1, state, prefix);
                return;
            }

            if (IsCollapsedContainer(node))
            {
                RenderChildren(node, depth, parentName, state, prefix);
                return;
            }
        }

        WriteLine(node, depth, state, prefix);

        // The parts of composite controls (slider thumbs, spinner buttons, combo box edit and
        // drop-down button) are implementation detail; the control's own line already has its state.
        if (!options.Full && node.ControlTypeId is ControlTypeIds.Slider or ControlTypeIds.Spinner or ControlTypeIds.ProgressBar or ControlTypeIds.ScrollBar)
        {
            return;
        }

        if (state.MaxDepth is { } maxDepth && depth >= maxDepth)
        {
            WriteCutOff(node, depth, state, prefix);
            return;
        }

        RenderBelow(node, depth, state, prefix);
    }

    private void RenderBelow(UiNode node, int depth, RenderState state, string prefix)
    {
        if (!options.Full && node.ControlTypeId == ControlTypeIds.ComboBox)
        {
            foreach (var item in node.Descendants().Where(d => d.ControlTypeId == ControlTypeIds.ListItem && (options.IncludeOffscreen || !d.IsOffscreen)).Take(options.MaxItemsPerContainer))
            {
                RenderNode(item, depth + 1, node.Name, state, prefix);
            }

            return;
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

        WriteOmittedItems(depth, state, childPrefix, hiddenItems, offscreen);
    }

    /// <summary>Renders the row actions of a row, with nested rows and items capped like a list.</summary>
    private void RenderRowActions(UiNode row, int depth, RenderState state, string prefix)
    {
        var shownItems = 0;
        var hiddenItems = 0;
        foreach (var action in RowActions(row))
        {
            if (IsItem(action) || IsGridRow(action))
            {
                if (shownItems >= options.MaxItemsPerContainer)
                {
                    hiddenItems++;
                    continue;
                }

                shownItems++;
            }

            RenderNode(action, depth, row.Name, state, prefix);
        }

        WriteOmittedItems(depth, state, prefix, hiddenItems, offscreen: 0);
    }

    private static void WriteOmittedItems(int depth, RenderState state, string prefix, int hiddenItems, int offscreen)
    {
        // A depth limit counts what a Snapshot from its node would list; this line is not an element.
        if ((hiddenItems == 0 && offscreen == 0) || state.Counting)
        {
            return;
        }

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
        state.Builder.Append(prefix).Append("... ").AppendJoin(", ", parts).AppendLine();
        state.Lines++;
    }

    /// <summary>
    /// Counts what a depth limit leaves out below <paramref name="node"/>: the elements a Snapshot with
    /// <c>root=</c> this node would list, with the same item limits and collapsing. The line always carries
    /// the node's Ref, even for text that gets none on its own line, so the Agent can continue from it.
    /// </summary>
    private void WriteCutOff(UiNode node, int depth, RenderState state, string prefix)
    {
        var counter = new RenderState { Counting = true };
        RenderBelow(node, depth, counter, prefix);
        var hidden = counter.Lines;
        if (hidden == 0)
        {
            return;
        }

        Indent(state.Builder, depth + 1);
        state.Builder.Append(prefix).Append(CultureInfo.InvariantCulture,
            $"... {hidden} more {(hidden == 1 ? "element" : "elements")} below (root={refs.GetOrAssign(node)})");
        state.Builder.AppendLine();
        state.Lines++;
    }

    private void WriteLine(UiNode node, int depth, RenderState state, string prefix, string? nameOverride = null, string? roleOverride = null)
    {
        if (state.Counting)
        {
            state.Lines++;
            return;
        }

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
            sb.Append(' ').Append(SelectorStep.FormatAutomationId(node.AutomationId));
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

    /// <summary>
    /// The controls inside a row that act on their own, outermost first. The row's summary line would
    /// hide them, and clicking the row only selects it, so they get lines with their own Refs. A row or
    /// item nested in the row (master-detail, a card with its own button bar) is returned as is, so it
    /// renders with its own summary and actions instead of flattening its controls into this row.
    /// </summary>
    private IEnumerable<UiNode> RowActions(UiNode node)
    {
        foreach (var child in node.Children)
        {
            if (!options.IncludeOffscreen && child.IsOffscreen)
            {
                continue;
            }

            if (IsRowAction(child) || IsNestedItem(child))
            {
                yield return child;
            }
            else if (!IsGridContainer(child))
            {
                foreach (var nested in RowActions(child))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>
    /// An element inside a row that can be invoked, toggled, selected as a radio button or edited. A
    /// grid's own cells stay in the row summary (an editable WPF cell has Invoke and Value), unless the
    /// cell is a control itself, like the button and check box cells of a WinForms DataGridView.
    /// </summary>
    internal static bool IsRowAction(UiNode node)
    {
        // UI Automation radio buttons expose SelectionItem, not Toggle.
        var acts = node.Has(UiPatterns.Invoke) || node.Has(UiPatterns.Toggle) ||
                   (node.ControlTypeId == ControlTypeIds.RadioButton && node.Has(UiPatterns.SelectionItem)) ||
                   (node.Has(UiPatterns.Value) && !node.ValueIsReadOnly);
        if (!acts)
        {
            return false;
        }

        return !IsCell(node) || node.ControlTypeId is ControlTypeIds.Button or ControlTypeIds.CheckBox or ControlTypeIds.RadioButton or ControlTypeIds.Hyperlink;
    }

    /// <summary>
    /// A row or item inside a row's subtree that belongs to a list of its own. The row's direct children
    /// are its cells or parts (a WinForms grid row holds DataItem cells), never nested items.
    /// </summary>
    private static bool IsNestedItem(UiNode node) =>
        (IsItem(node) || IsGridRow(node)) && node.Parent is { } parent && !IsGridRow(parent);

    /// <summary>A cell of a grid row: a direct child of a row that sits in a DataGrid or Table.</summary>
    private static bool IsCell(UiNode node) => node.Parent is { } row && IsGridRow(row) && IsGridContainer(row.Parent);

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

    /// <summary>Elements the current view would show: every one in a full Snapshot, the reportable ones otherwise.</summary>
    private int CountVisible(UiNode node) =>
        node.DescendantsAndSelf().Count(n => (options.IncludeOffscreen || !n.IsOffscreen) && (options.Full || IsReportable(n)));

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

        /// <summary>Levels below the roots; null renders all of them. Only Snapshots set it.</summary>
        public int? MaxDepth { get; init; }

        /// <summary>Counts the element lines without writing them or assigning Refs.</summary>
        public bool Counting { get; init; }
    }
}
