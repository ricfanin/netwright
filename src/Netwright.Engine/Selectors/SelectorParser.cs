using System.Globalization;
using System.Text;
using Netwright.Engine.Model;

namespace Netwright.Engine.Selectors;

/// <summary>
/// Parses Selectors.
/// <code>
/// selector := step (">>" step)*
/// step     := role? ("#" automationId)? (("~")? '"' name '"')? (":nth(" n ")")?
/// </code>
/// Names match case-insensitively; <c>"x"</c> is an exact match and <c>~"x"</c> a substring match.
/// <c>:nth(n)</c> is 1-based.
/// </summary>
public static class SelectorParser
{
    public const string Syntax =
        "Selector syntax: #automationId | role \"exact name\" | role ~\"partial name\" | step:nth(2) | step >> step (e.g. window \"Confirm\" >> button \"Yes\").";

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
            automationId = reader.ReadWhile(c => !char.IsWhiteSpace(c) && c != '"' && c != '~' && c != ':' && c != '>');
            if (automationId.Length == 0)
            {
                throw Invalid(text, "'#' must be followed by an AutomationId");
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

        if (role is null && automationId is null && name is null)
        {
            throw Invalid(text, $"expected a role, #automationId or \"name\" at position {reader.Position + 1}");
        }

        return new SelectorStep(role, automationId, name, match, nth);
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
            }

            sb.Append(reader.Peek);
            reader.Advance();
        }

        if (reader.AtEnd)
        {
            throw Invalid(text, "unterminated quoted name");
        }

        reader.Advance();
        return sb.ToString();
    }

    private static NetwrightException Invalid(string text, string problem, string? extra = null) =>
        new(ErrorCodes.InvalidSelector, $"Invalid selector '{text}': {problem}.", extra is null ? Syntax : $"{extra} {Syntax}");

    private sealed class Reader(string text)
    {
        public int Position { get; set; }

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

        public bool TryConsume(string token)
        {
            if (string.CompareOrdinal(text, Position, token, 0, token.Length) == 0)
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
