using System.Globalization;
using System.Text;
using Netwright.Engine.Model;

namespace Netwright.Engine.Selectors;

/// <summary>
/// Parses Selectors.
/// <code>
/// selector := step (">>" step)*
/// step     := role? ("#" (automationId | '"' automationId '"'))? (("~")? '"' name '"')?
///             ("[value" ("=" | "~=") '"' value '"' "]")? (":nth(" n ")")?
/// </code>
/// Names and values match case-insensitively; <c>"x"</c> and <c>[value="x"]</c> are exact matches,
/// <c>~"x"</c> and <c>[value~="x"]</c> substring matches. <c>:nth(n)</c> is 1-based.
/// </summary>
public static class SelectorParser
{
    public const string Syntax =
        "Selector syntax: #automationId | #\"id with spaces\" | role \"exact name\" | role ~\"partial name\" | [value=\"exact value\"] | [value~=\"partial value\"] | step:nth(2) | step >> step (e.g. window \"Confirm\" >> button \"Yes\", #Customer[value=\"Rossi\"]).";

    private const string ValueExact = "[value=";
    private const string ValueContains = "[value~=";

    public static Selector Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var steps = new List<SelectorStep>();
        var reader = new Reader(text);

        reader.SkipWhitespace();
        if (reader.AtEnd)
        {
            throw Invalid(text, "the selector is empty");
        }

        while (true)
        {
            steps.Add(ParseStep(reader, text));
            reader.SkipWhitespace();
            if (reader.AtEnd)
            {
                break;
            }

            if (!reader.TryConsume(">>"))
            {
                throw Invalid(text, $"unexpected '{reader.Peek}' at position {reader.Position + 1}");
            }

            reader.SkipWhitespace();
            if (reader.AtEnd)
            {
                throw Invalid(text, "'>>' must be followed by another step");
            }
        }

        return new Selector(steps);
    }

    private static SelectorStep ParseStep(Reader reader, string text)
    {
        string? role = null;
        string? automationId = null;
        string? name = null;
        var match = NameMatch.Exact;
        string? value = null;
        var valueMatch = NameMatch.Exact;
        int? nth = null;

        if (char.IsLetter(reader.Peek))
        {
            var word = reader.ReadWhile(char.IsLetter);
            role = Roles.Normalize(word) ?? throw Invalid(
                text,
                $"unknown role '{word}'",
                $"Known roles: {string.Join(", ", Roles.All)}.");
        }

        if (!reader.AtEnd && reader.Peek == '#')
        {
            reader.Advance();
            automationId = !reader.AtEnd && reader.Peek == '"'
                ? ReadQuoted(reader, text)
                : ReadUnquotedAutomationId(reader);
            if (automationId.Length == 0)
            {
                throw Invalid(text, "'#' must be followed by an AutomationId (quote it when it contains spaces: #\"my id\")");
            }
        }

        var checkpoint = reader.Position;
        reader.SkipWhitespace();
        if (!reader.AtEnd && (reader.Peek == '"' || reader.Peek == '~'))
        {
            if (reader.Peek == '~')
            {
                match = NameMatch.Contains;
                reader.Advance();
            }

            name = ReadQuoted(reader, text);
        }
        else
        {
            reader.Position = checkpoint;
        }

        checkpoint = reader.Position;
        reader.SkipWhitespace();
        var contains = reader.TryConsume(ValueContains);
        if (contains || reader.TryConsume(ValueExact))
        {
            valueMatch = contains ? NameMatch.Contains : NameMatch.Exact;
            if (reader.AtEnd || reader.Peek != '"')
            {
                throw Invalid(text, "[value=...] needs a quoted value, e.g. [value=\"ORD-4711\"]");
            }

            value = ReadQuoted(reader, text);
            if (!reader.TryConsume("]"))
            {
                throw Invalid(text, "[value=\"...\" must be closed with ]");
            }
        }
        else
        {
            reader.Position = checkpoint;
        }

        if (reader.TryConsume(":nth("))
        {
            var digits = reader.ReadWhile(char.IsDigit);
            if (digits.Length == 0 || !reader.TryConsume(")"))
            {
                throw Invalid(text, ":nth() needs a positive number, e.g. :nth(2)");
            }

            nth = int.Parse(digits, CultureInfo.InvariantCulture);
            if (nth < 1)
            {
                throw Invalid(text, ":nth() is 1-based");
            }
        }

        if (role is null && automationId is null && name is null && value is null)
        {
            throw Invalid(text, $"expected a role, #automationId, \"name\" or [value=\"...\"] at position {reader.Position + 1}");
        }

        return new SelectorStep(role, automationId, name, match, nth) { Value = value, ValueMatch = valueMatch };
    }

    /// <summary>A character that ends an unquoted <c>#id</c>; an id containing one must be printed quoted.</summary>
    internal static bool EndsUnquotedAutomationId(char c) => char.IsWhiteSpace(c) || c is '"' or '~' or ':' or '>';

    /// <summary>
    /// An unquoted <c>#id</c> also ends where <c>[value=</c> or <c>[value~=</c> begins. Any other <c>[</c>
    /// belongs to the id, so Selectors like <c>#Item[3]</c> keep parsing as before.
    /// </summary>
    private static string ReadUnquotedAutomationId(Reader reader)
    {
        var start = reader.Position;
        while (!reader.AtEnd && !EndsUnquotedAutomationId(reader.Peek) &&
               !reader.LookingAt(ValueExact) && !reader.LookingAt(ValueContains))
        {
            reader.Advance();
        }

        return reader.Text[start..reader.Position];
    }

    private static string ReadQuoted(Reader reader, string text)
    {
        if (reader.AtEnd || reader.Peek != '"')
        {
            throw Invalid(text, "'~' must be followed by a quoted name");
        }

        reader.Advance();
        var sb = new StringBuilder();
        while (!reader.AtEnd && reader.Peek != '"')
        {
            if (reader.Peek == '\\' && reader.Position + 1 < reader.Length)
            {
                reader.Advance();
                sb.Append(reader.Peek switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    var c => c,
                });
            }
            else
            {
                sb.Append(reader.Peek);
            }

            reader.Advance();
        }

        if (reader.AtEnd)
        {
            throw Invalid(text, "unterminated quoted string");
        }

        reader.Advance();
        return sb.ToString();
    }

    private static NetwrightException Invalid(string text, string problem, string? extra = null) =>
        new(ErrorCodes.InvalidSelector, $"Invalid selector '{text}': {problem}.", extra is null ? Syntax : $"{extra} {Syntax}");

    private sealed class Reader(string text)
    {
        public int Position { get; set; }

        public string Text => text;

        public int Length => text.Length;

        public bool AtEnd => Position >= text.Length;

        public char Peek => text[Position];

        public void Advance() => Position++;

        public void SkipWhitespace()
        {
            while (!AtEnd && char.IsWhiteSpace(Peek))
            {
                Position++;
            }
        }

        public bool LookingAt(string token) => string.CompareOrdinal(text, Position, token, 0, token.Length) == 0;

        public bool TryConsume(string token)
        {
            if (LookingAt(token))
            {
                Position += token.Length;
                return true;
            }

            return false;
        }

        public string ReadWhile(Func<char, bool> predicate)
        {
            var start = Position;
            while (!AtEnd && predicate(Peek))
            {
                Position++;
            }

            return text[start..Position];
        }
    }
}
