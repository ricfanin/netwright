using Netwright.Engine.Model;

namespace Netwright.Engine.Selectors;

/// <summary>
/// Produces the shortest robust Selector that uniquely identifies an element, preferring
/// AutomationIds (which Developers control) over visible names (which change with localization).
/// </summary>
public static class SelectorBuilder
{
    public static Selector Build(UiNode node, UiTree tree)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(tree);

        var own = OwnSteps(node).ToList();

        // 1. A step on its own that is unique across the whole app.
        foreach (var step in own)
        {
            var selector = new Selector([step]);
            if (IsUniqueMatch(selector, tree, node))
            {
                return selector;
            }
        }

        // 2. Scoped under the nearest ancestor that can itself be identified uniquely.
        foreach (var ancestor in node.Ancestors())
        {
            var anchor = OwnSteps(ancestor).FirstOrDefault(s => IsUniqueMatch(new Selector([s]), tree, ancestor));
            if (anchor is null)
            {
                continue;
            }

            foreach (var step in own)
            {
                var selector = new Selector([anchor, step]);
                if (IsUniqueMatch(selector, tree, node))
                {
                    return selector;
                }
            }

            var positional = WithNth(new Selector([anchor, own[0]]), tree, node);
            if (positional is not null)
            {
                return positional;
            }
        }

        // 3. Global position as a last resort.
        return WithNth(new Selector([own[0]]), tree, node) ?? new Selector([own[0]]);
    }

    private static IEnumerable<SelectorStep> OwnSteps(UiNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.AutomationId) && !LooksGenerated(node.AutomationId))
        {
            yield return new SelectorStep(null, node.AutomationId, null, NameMatch.Exact, null);
        }

        if (!string.IsNullOrWhiteSpace(node.Name) && node.Name.Length <= 80)
        {
            yield return new SelectorStep(node.Role, null, node.Name.Trim(), NameMatch.Exact, null);
        }

        if (!string.IsNullOrWhiteSpace(node.AutomationId) && LooksGenerated(node.AutomationId))
        {
            yield return new SelectorStep(null, node.AutomationId, null, NameMatch.Exact, null);
        }

        yield return new SelectorStep(node.Role, null, null, NameMatch.Exact, null);
    }

    /// <summary>WPF and WinUI RuntimeId-like or numeric ids are not stable between runs.</summary>
    private static bool LooksGenerated(string automationId) =>
        automationId.All(char.IsDigit) || automationId.StartsWith("Item ", StringComparison.Ordinal);

    private static bool IsUniqueMatch(Selector selector, UiTree tree, UiNode node)
    {
        var matches = SelectorMatcher.Match(selector, tree);
        return matches.Count == 1 && matches[0].Key == node.Key;
    }

    private static Selector? WithNth(Selector selector, UiTree tree, UiNode node)
    {
        var matches = SelectorMatcher.Match(selector, tree);
        for (var i = 0; i < matches.Count; i++)
        {
            if (matches[i].Key == node.Key)
            {
                var steps = selector.Steps.ToList();
                steps[^1] = steps[^1] with { Nth = i + 1 };
                return new Selector(steps);
            }
        }

        return null;
    }
}
