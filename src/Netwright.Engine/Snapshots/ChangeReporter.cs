using System.Globalization;
using System.Text;
using Netwright.Engine.Model;
using Netwright.Engine.Refs;

namespace Netwright.Engine.Snapshots;

public sealed record ChangeReport(string Text, int Changes, bool Truncated)
{
    public bool IsEmpty => Changes == 0;
}

/// <summary>
/// Compares the Target App before and after an action and describes what the Agent would notice:
/// elements that appeared (<c>+</c>), disappeared (<c>-</c>) or changed (<c>~</c>), plus focus moves.
/// Elements are matched by RuntimeId, so a Ref in the report is the same Ref the Agent already knows.
/// An element that got a new RuntimeId but kept its role, AutomationId, name and children is matched too.
/// </summary>
public static class ChangeReporter
{
    /// <param name="before">The Target App before the action.</param>
    /// <param name="after">The Target App once it Settled.</param>
    /// <param name="renderer">Renders lines and issues Refs.</param>
    /// <param name="maxLines">Line budget of the report.</param>
    /// <param name="actionTargetKey">The element the action was performed on; focus moving to it is not worth reporting.</param>
    public static ChangeReport Build(UiTree before, UiTree after, SnapshotRenderer renderer, int maxLines = 40, string? actionTargetKey = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(renderer);

        var beforeVisible = VisibleReportable(before, renderer);
        var afterVisible = VisibleReportable(after, renderer);
        var renewed = PairRenewed(beforeVisible, afterVisible, renderer.Refs);
        var renewedFrom = new HashSet<string>(renewed.Values, StringComparer.Ordinal);
        string BeforeKey(string key) => renewed.GetValueOrDefault(key, key);

        var sb = new StringBuilder();
        var lines = 0;
        var changes = 0;
        var omitted = 0;

        void Emit(Action<StringBuilder> write, int cost = 1)
        {
            changes++;
            if (lines + cost > maxLines)
            {
                omitted++;
                return;
            }

            write(sb);
            lines += cost;
        }

        var addedRoots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in after.AllNodes)
        {
            if (!afterVisible.ContainsKey(node.Key))
            {
                continue;
            }

            var ancestorAdded = node.Ancestors().Any(a => addedRoots.Contains(a.Key));
            if (!beforeVisible.TryGetValue(BeforeKey(node.Key), out var previous))
            {
                if (ancestorAdded)
                {
                    continue;
                }

                addedRoots.Add(node.Key);
                var budget = Math.Max(1, maxLines - lines);
                if (lines >= maxLines)
                {
                    changes++;
                    omitted++;
                    continue;
                }

                changes++;
                lines += renderer.RenderInto(sb, node, "+ ", 0, budget, node.Parent?.Name);
                continue;
            }

            if (ancestorAdded)
            {
                continue;
            }

            if (HasObservableChange(previous, node))
            {
                Emit(b => b.Append("~ ").AppendLine(renderer.RenderLine(node, withFocus: false)));
            }
        }

        var removedRoots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in before.AllNodes)
        {
            if (!beforeVisible.ContainsKey(node.Key) || afterVisible.ContainsKey(node.Key) || renewedFrom.Contains(node.Key))
            {
                continue;
            }

            if (node.Ancestors().Any(a => removedRoots.Contains(a.Key)))
            {
                continue;
            }

            removedRoots.Add(node.Key);
            Emit(b => b.Append("- ").AppendLine(renderer.RenderLine(node, withRef: false)));
        }

        var focusBefore = before.Focused;
        var focusAfter = after.Focused;
        if (focusAfter is not null && BeforeKey(focusAfter.Key) != focusBefore?.Key && BeforeKey(focusAfter.Key) != actionTargetKey &&
            focusAfter.ControlTypeId != Uia.ControlTypeIds.Window)
        {
            Emit(b => b.Append("focus: ").AppendLine(renderer.RenderLine(focusAfter, withAttributes: false)));
        }

        if (omitted > 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $"... {omitted} more changes. Take a desktop_snapshot to see the full screen.");
            sb.AppendLine();
        }

        return new ChangeReport(sb.ToString().TrimEnd(), changes, omitted > 0);
    }

    private static Dictionary<string, UiNode> VisibleReportable(UiTree tree, SnapshotRenderer renderer)
    {
        var result = new Dictionary<string, UiNode>(StringComparer.Ordinal);
        foreach (var window in tree.Windows)
        {
            Collect(window, renderer, result);
        }

        return result;
    }

    private static void Collect(UiNode node, SnapshotRenderer renderer, Dictionary<string, UiNode> result)
    {
        if (node.IsOffscreen && node.Parent is not null && !renderer.Options.IncludeOffscreen)
        {
            return;
        }

        if (renderer.IsReportable(node))
        {
            result.TryAdd(node.Key, node);
        }

        foreach (var child in node.Children)
        {
            Collect(child, renderer, result);
        }
    }

    /// <summary>
    /// Some providers give an element a new RuntimeId when they re-create it although nothing about it
    /// changed, like the inner pane of a DevExpress HamburgerMenu on every navigation, while its children
    /// keep theirs. Such an element would be reported as removed and added again with all its children.
    /// A removed and an added element with the same role, AutomationId, name and children are paired
    /// instead, and the Refs the Agent holds move to the new key. An element without children is not
    /// paired: nothing but its name shows that it is the same element.
    /// </summary>
    /// <returns>The before key of each paired element, by its after key.</returns>
    private static Dictionary<string, string> PairRenewed(Dictionary<string, UiNode> beforeVisible, Dictionary<string, UiNode> afterVisible, RefRegistry refs)
    {
        var renewed = new Dictionary<string, string>(StringComparer.Ordinal);
        var removedByFirstChild = new Dictionary<string, UiNode>(StringComparer.Ordinal);
        foreach (var (key, node) in beforeVisible)
        {
            if (node.Children.Count > 0 && !afterVisible.ContainsKey(key))
            {
                removedByFirstChild.TryAdd(node.Children[0].Key, node);
            }
        }

        if (removedByFirstChild.Count == 0)
        {
            return renewed;
        }

        foreach (var (key, node) in afterVisible)
        {
            if (node.Children.Count > 0 && !beforeVisible.ContainsKey(key) &&
                removedByFirstChild.TryGetValue(node.Children[0].Key, out var old) && IsSameElement(old, node))
            {
                renewed[key] = old.Key;
                refs.Rebind(old.Key, key);
            }
        }

        return renewed;
    }

    private static bool IsSameElement(UiNode before, UiNode after) =>
        before.ControlTypeId == after.ControlTypeId &&
        string.Equals(before.AutomationId, after.AutomationId, StringComparison.Ordinal) &&
        string.Equals(before.Name, after.Name, StringComparison.Ordinal) &&
        before.Children.Select(c => c.Key).SequenceEqual(after.Children.Select(c => c.Key), StringComparer.Ordinal);

    private static bool HasObservableChange(UiNode before, UiNode after) =>
        !string.Equals(before.Name, after.Name, StringComparison.Ordinal) ||
        !string.Equals(before.Value, after.Value, StringComparison.Ordinal) ||
        before.IsEnabled != after.IsEnabled ||
        before.Toggle != after.Toggle ||
        before.IsSelected != after.IsSelected ||
        before.Expand != after.Expand ||
        before.RangeValue != after.RangeValue ||
        before.GridRowCount != after.GridRowCount ||
        before.IsModal != after.IsModal;
}
