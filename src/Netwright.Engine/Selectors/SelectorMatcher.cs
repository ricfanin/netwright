using Netwright.Engine.Model;

namespace Netwright.Engine.Selectors;

/// <summary>Evaluates Selectors against a captured <see cref="UiTree"/>.</summary>
public static class SelectorMatcher
{
    /// <summary>Returns every element matching the selector, in document order.</summary>
    public static IReadOnlyList<UiNode> Match(Selector selector, UiTree tree)
    {
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(tree);

        IReadOnlyList<UiNode> scope = tree.Windows;
        var first = true;

        foreach (var step in selector.Steps)
        {
            var candidates = first
                ? scope.SelectMany(w => w.DescendantsAndSelf())
                : scope.SelectMany(s => s.Descendants());

            var matches = Distinct(candidates.Where(n => Matches(step, n)));
            if (step.Nth is { } nth)
            {
                matches = nth <= matches.Count ? [matches[nth - 1]] : [];
            }

            scope = matches;
            first = false;
            if (scope.Count == 0)
            {
                break;
            }
        }

        return scope;
    }

    public static bool Matches(SelectorStep step, UiNode node)
    {
        if (step.Role is not null && step.Role != "element" && node.Role != step.Role)
        {
            return false;
        }

        if (step.AutomationId is not null && !string.Equals(node.AutomationId, step.AutomationId, StringComparison.Ordinal))
        {
            return false;
        }

        if (step.Name is not null)
        {
            var name = node.Name.Trim();
            var wanted = step.Name.Trim();
            var ok = step.NameMatch == NameMatch.Exact
                ? string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase)
                : name.Contains(wanted, StringComparison.OrdinalIgnoreCase);
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static List<UiNode> Distinct(IEnumerable<UiNode> nodes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<UiNode>();
        foreach (var node in nodes)
        {
            if (seen.Add(node.Key))
            {
                result.Add(node);
            }
        }

        return result;
    }
}
