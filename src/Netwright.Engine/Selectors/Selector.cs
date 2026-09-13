using System.Globalization;
using System.Text;

namespace Netwright.Engine.Selectors;

public enum NameMatch
{
    Exact,
    Contains,
}

/// <summary>One segment of a Selector, e.g. <c>button#btnSave "Save":nth(2)</c>.</summary>
public sealed record SelectorStep(string? Role, string? AutomationId, string? Name, NameMatch NameMatch, int? Nth)
{
    public override string ToString()
    {
        var sb = new StringBuilder();
        if (Role is not null)
        {
            sb.Append(Role);
        }

        if (AutomationId is not null)
        {
            sb.Append('#').Append(AutomationId);
        }

        if (Name is not null)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            if (NameMatch == NameMatch.Contains)
            {
                sb.Append('~');
            }

            sb.Append('"').Append(Name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
        }

        if (Nth is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $":nth({Nth})");
        }

        return sb.ToString();
    }
}

/// <summary>
/// A short textual expression that identifies an element without a prior Snapshot.
/// Steps separated by <c>&gt;&gt;</c> narrow the search to descendants of the previous step.
/// </summary>
public sealed record Selector(IReadOnlyList<SelectorStep> Steps)
{
    public override string ToString() => string.Join(" >> ", Steps);
}
