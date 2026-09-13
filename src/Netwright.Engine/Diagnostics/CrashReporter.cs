using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.Text;

namespace Netwright.Engine.Diagnostics;

/// <summary>
/// Builds the Crash Report for a Target App that exited unexpectedly. The .NET runtime writes the
/// unhandled exception, with its stack trace, to the Windows Application event log (event 1026)
/// for both .NET Framework and modern .NET apps.
/// </summary>
internal static class CrashReporter
{
    private const int MaxStackLines = 12;

    public static async Task<string> DescribeAsync(string executableName, int? exitCode, IReadOnlyList<string> stderrTail, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder("! Target App exited unexpectedly");
        if (exitCode is { } code)
        {
            sb.Append(CultureInfo.InvariantCulture, $" (exit code 0x{code:X8}{(code == unchecked((int)0xE0434352) ? ", unhandled .NET exception" : "")})");
        }

        sb.AppendLine();

        var eventText = await FindRuntimeEventAsync(executableName, cancellationToken).ConfigureAwait(false);
        var exception = eventText is null ? ExtractFromStderr(stderrTail) : ExtractFromEvent(eventText);
        if (exception.Count > 0)
        {
            foreach (var line in exception.Take(MaxStackLines + 1))
            {
                sb.Append("  ").AppendLine(line.TrimEnd());
            }

            if (exception.Count > MaxStackLines + 1)
            {
                sb.Append(CultureInfo.InvariantCulture, $"  ... {exception.Count - MaxStackLines - 1} more lines (desktop_logs shows stderr)").AppendLine();
            }
        }
        else if (stderrTail.Count > 0)
        {
            sb.AppendLine("  Last stderr lines:");
            foreach (var line in stderrTail.TakeLast(8))
            {
                sb.Append("  ").AppendLine(line);
            }
        }

        sb.Append("  The session no longer has a Target App; launch or attach again.");
        return sb.ToString();
    }

    private static async Task<string?> FindRuntimeEventAsync(string executableName, CancellationToken cancellationToken)
    {
        // The runtime writes the event right before the process dies; allow for log latency.
        for (var attempt = 0; attempt < 15; attempt++)
        {
            var text = TryReadEvent(executableName);
            if (text is not null)
            {
                return text;
            }

            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static string? TryReadEvent(string executableName)
    {
        try
        {
            const string query = "*[System[Provider[@Name='.NET Runtime'] and (EventID=1026) and TimeCreated[timediff(@SystemTime) <= 60000]]]";
            using var reader = new EventLogReader(new EventLogQuery("Application", PathType.LogName, query) { ReverseDirection = true });
            for (var record = reader.ReadEvent(); record is not null; record = reader.ReadEvent())
            {
                using (record)
                {
                    var text = record.FormatDescription() ?? string.Join(Environment.NewLine, record.Properties.Select(p => p.Value?.ToString()));
                    if (text.Contains(executableName, StringComparison.OrdinalIgnoreCase))
                    {
                        return text;
                    }
                }
            }
        }
        catch (EventLogException)
        {
            // The log is not readable for this user; fall back to stderr.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    internal static List<string> ExtractFromEvent(string eventText)
    {
        var lines = eventText.Replace("\r", "", StringComparison.Ordinal).Split('\n').ToList();
        var start = lines.FindIndex(l => l.StartsWith("Exception Info:", StringComparison.Ordinal));
        if (start < 0)
        {
            return lines.Where(l => l.Length > 0).ToList();
        }

        var result = new List<string> { lines[start]["Exception Info:".Length..].Trim() };
        result.AddRange(lines.Skip(start + 1).Where(l => l.Trim().Length > 0));
        return result;
    }

    internal static List<string> ExtractFromStderr(IReadOnlyList<string> stderr)
    {
        var start = -1;
        for (var i = 0; i < stderr.Count; i++)
        {
            if (stderr[i].StartsWith("Unhandled exception.", StringComparison.Ordinal) || stderr[i].Contains("Exception: ", StringComparison.Ordinal))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return [];
        }

        var result = stderr.Skip(start).ToList();
        if (result[0].StartsWith("Unhandled exception. ", StringComparison.Ordinal))
        {
            result[0] = result[0]["Unhandled exception. ".Length..];
        }

        return result;
    }
}
