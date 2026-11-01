using System.Globalization;
using Netwright.Engine.Session;

namespace Netwright;

/// <summary>Command-line and environment configuration of the server.</summary>
internal sealed record ServerOptions(SessionOptions Session, bool ShowHelp, bool ShowVersion)
{
    public const string Usage = """
        Usage: netwright [options]

        An MCP server (stdio) that lets AI agents operate .NET desktop apps.

        Options:
          --allow <pattern>         Only allow launching/attaching apps whose path or name matches.
                                    Wildcards * and ? are supported; repeat for several patterns.
                                    Also read from NETWRIGHT_ALLOW (separated by ';').
          --action-timeout <ms>     How long actions wait for an element to be actionable (default 5000).
          --settle-timeout <ms>     Upper bound for waiting until the UI settles (default 3000).
          --max-snapshot-lines <n>  Line budget of a snapshot (default 300).
          --screenshot-dir <dir>    Let desktop_screenshot save PNGs (path=) inside this directory.
                                    Also read from NETWRIGHT_SCREENSHOT_DIR.
          --version                 Print the version.
          --help                    Show this help.
        """;

    public static ServerOptions Parse(string[] args, Func<string, string?> environment)
    {
        var session = new SessionOptions();
        var allowed = new List<string>();
        var help = false;
        var version = false;

        if (environment("NETWRIGHT_SCREENSHOT_DIR") is { Length: > 0 } screenshotDirectory)
        {
            session = session with { ScreenshotDirectory = FullPath("NETWRIGHT_SCREENSHOT_DIR", screenshotDirectory) };
        }

        if (environment("NETWRIGHT_ALLOW") is { Length: > 0 } fromEnvironment)
        {
            allowed.AddRange(fromEnvironment.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--allow":
                    allowed.Add(Value(args, ref i));
                    break;
                case "--action-timeout":
                    session = session with { ActionTimeoutMs = PositiveInt(args, ref i) };
                    break;
                case "--settle-timeout":
                    session = session with { SettleTimeoutMs = PositiveInt(args, ref i) };
                    break;
                case "--max-snapshot-lines":
                    session = session with { Snapshot = session.Snapshot with { MaxLines = PositiveInt(args, ref i) } };
                    break;
                case "--screenshot-dir":
                    session = session with { ScreenshotDirectory = FullPath(args[i], Value(args, ref i)) };
                    break;
                case "--help" or "-h" or "-?":
                    help = true;
                    break;
                case "--version":
                    version = true;
                    break;
                default:
                    throw new ArgumentException($"unknown option '{args[i]}'");
            }
        }

        return new ServerOptions(session with { AllowedApps = allowed }, help, version);
    }

    private static string Value(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
        {
            throw new ArgumentException($"{args[i]} needs a value");
        }

        return args[++i];
    }

    private static int PositiveInt(string[] args, ref int i)
    {
        var name = args[i];
        var text = Value(args, ref i);
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            throw new ArgumentException($"{name} needs a positive number, got '{text}'");
        }

        return value;
    }

    /// <summary>Resolved once at startup, so a relative directory means the one the server was started in.</summary>
    private static string FullPath(string name, string text)
    {
        try
        {
            return Path.GetFullPath(text);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"{name} needs a directory path, got '{text}'", ex);
        }
    }
}
