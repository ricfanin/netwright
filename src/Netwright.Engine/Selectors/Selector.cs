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
            sb.Append(FormatAutomationId(AutomationId));
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

            sb.Append(Quote(Name));
        }

        if (Nth is not null)
        {
            sb.Append(CultureInfo.InvariantCulture, $":nth({Nth})");
        }

        return sb.ToString();
    }

    /// <summary>
    /// <c>#id</c>, or <c>#"id"</c> when the id contains a character that would end it, such as a space
    /// (<c>Item 3</c>, or DevExpress ids like <c>Clear Filter Button</c>), so a printed id parses back.
    /// An empty id has no Selector form (the parser rejects <c>#""</c>), so it is an argument error.
    /// </summary>
    internal static string FormatAutomationId(string automationId)
    {
        ArgumentException.ThrowIfNullOrEmpty(automationId);
        return !automationId.Any(SelectorParser.EndsUnquotedAutomationId)
            ? "#" + automationId
            : "#" + Quote(automationId);
    }

    /// <summary>
    /// <c>"text"</c> with <c>\</c> and <c>"</c> escaped, and line breaks as <c>\n</c> and <c>\r</c> so the
    /// Selector stays on one Snapshot line. <see cref="SelectorParser"/> reads all four back.
    /// </summary>
    internal static string Quote(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal) + "\"";
}

/// <summary>
/// A short textual expression that identifies an element without a prior Snapshot.
/// Steps separated by <c>&gt;&gt;</c> narrow the search to descendants of the previous step.
/// </summary>
public sealed record Selector(IReadOnlyList<SelectorStep> Steps)
{
    public override string ToString() => string.Join(" >> ", Steps);
}
