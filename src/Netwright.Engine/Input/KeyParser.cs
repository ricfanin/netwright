namespace Netwright.Engine.Input;

/// <summary>
/// Parses key specifications such as <c>Enter</c>, <c>Ctrl+S</c> or <c>Tab Tab Enter</c> into chords
/// pressed one after another.
/// </summary>
public static class KeyParser
{
    private static readonly Dictionary<string, VirtualKeyShort> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = VirtualKeyShort.CONTROL,
        ["control"] = VirtualKeyShort.CONTROL,
        ["alt"] = VirtualKeyShort.ALT,
        ["shift"] = VirtualKeyShort.SHIFT,
        ["win"] = VirtualKeyShort.LWIN,
        ["meta"] = VirtualKeyShort.LWIN,
        ["enter"] = VirtualKeyShort.RETURN,
        ["return"] = VirtualKeyShort.RETURN,
        ["tab"] = VirtualKeyShort.TAB,
        ["escape"] = VirtualKeyShort.ESCAPE,
        ["esc"] = VirtualKeyShort.ESCAPE,
        ["space"] = VirtualKeyShort.SPACE,
        ["backspace"] = VirtualKeyShort.BACK,
        ["delete"] = VirtualKeyShort.DELETE,
        ["del"] = VirtualKeyShort.DELETE,
        ["insert"] = VirtualKeyShort.INSERT,
        ["home"] = VirtualKeyShort.HOME,
        ["end"] = VirtualKeyShort.END,
        ["pageup"] = VirtualKeyShort.PRIOR,
        ["pagedown"] = VirtualKeyShort.NEXT,
        ["up"] = VirtualKeyShort.UP,
        ["down"] = VirtualKeyShort.DOWN,
        ["left"] = VirtualKeyShort.LEFT,
        ["right"] = VirtualKeyShort.RIGHT,
        ["arrowup"] = VirtualKeyShort.UP,
        ["arrowdown"] = VirtualKeyShort.DOWN,
        ["arrowleft"] = VirtualKeyShort.LEFT,
        ["arrowright"] = VirtualKeyShort.RIGHT,
        ["apps"] = VirtualKeyShort.APPS,
        ["contextmenu"] = VirtualKeyShort.APPS,
    };

    public const string Syntax = "Keys: Enter, Tab, Escape, Space, Backspace, Delete, Home, End, PageUp, PageDown, Up/Down/Left/Right, F1-F12, A-Z, 0-9, combined with Ctrl/Alt/Shift/Win using '+' (Ctrl+S); separate presses with spaces (Tab Tab Enter).";

    public static IReadOnlyList<VirtualKeyShort[]> Parse(string keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var chords = new List<VirtualKeyShort[]>();

        foreach (var chordText in keys.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = chordText.Split('+', StringSplitOptions.TrimEntries);
            var chord = new VirtualKeyShort[parts.Length];
            for (var i = 0; i < parts.Length; i++)
            {
                chord[i] = ParseKey(parts[i]) ?? throw NetwrightException.InvalidArgument($"Unknown key '{parts[i]}' in '{keys}'.", Syntax);
            }

            chords.Add(chord);
        }

        if (chords.Count == 0)
        {
            throw NetwrightException.InvalidArgument("No keys given.", Syntax);
        }

        return chords;
    }

    private static VirtualKeyShort? ParseKey(string key)
    {
        if (Named.TryGetValue(key, out var named))
        {
            return named;
        }

        if (key.Length == 1)
        {
            var c = char.ToUpperInvariant(key[0]);
            if (c is >= 'A' and <= 'Z')
            {
                return (VirtualKeyShort)((int)VirtualKeyShort.KEY_A + (c - 'A'));
            }

            if (c is >= '0' and <= '9')
            {
                return (VirtualKeyShort)((int)VirtualKeyShort.KEY_0 + (c - '0'));
            }
        }

        if (key.Length is 2 or 3 && (key[0] == 'F' || key[0] == 'f') && int.TryParse(key.AsSpan(1), out var n) && n is >= 1 and <= 24)
        {
            return (VirtualKeyShort)((int)VirtualKeyShort.F1 + (n - 1));
        }

        return null;
    }
}
