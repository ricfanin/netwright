using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;

namespace Netwright.Engine.Companion;

/// <summary>Outcome of asking the Companion to commit pending bindings.</summary>
internal enum CommitResult
{
    /// <summary>The Companion answered; the count says how many bindings it wrote.</summary>
    Committed,

    /// <summary>The Companion runs but does not know the element's UI stack (for example WinUI).</summary>
    Unsupported,

    /// <summary>No Companion answered: an attached app, .NET Framework, .NET before 8, or a failure.</summary>
    Unreachable,
}

/// <summary>
/// Talks to the Companion: the small assembly that Target Apps launched by the session load through a
/// .NET startup hook (ADR 0007). It does in-process what UI Automation cannot do in the Background:
/// run patterns without letting the app take the foreground, and commit bindings that wait for focus
/// to leave a text field.
/// </summary>
internal static class CompanionClient
{
    public const string AssemblyFileName = "Netwright.Companion.dll";
    private const string StartupHooksVariable = "DOTNET_STARTUP_HOOKS";

    /// <summary>Must match <c>Netwright.Companion.HiddenWindows.Variable</c>.</summary>
    private const string HiddenVariable = "NETWRIGHT_HIDDEN";

    /// <summary>Full path of the Companion assembly shipped next to the Engine, or null if it is missing.</summary>
    public static string? AssemblyPath { get; } = Locate();

    /// <summary>Must match <c>Netwright.Companion.CompanionServer.PipeName</c>.</summary>
    public static string PipeName(int processId) => "netwright-companion-" + processId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Makes the process load the Companion, keeping startup hooks that are already set. With
    /// <paramref name="hidden"/> the Companion cloaks the app's windows (ADR 0008).
    /// </summary>
    public static bool AddTo(ProcessStartInfo startInfo, bool hidden)
    {
        if (AssemblyPath is null)
        {
            return false;
        }

        // Set or cleared explicitly, so the app never inherits the choice made for another launch.
        if (hidden)
        {
            startInfo.Environment[HiddenVariable] = "1";
        }
        else
        {
            startInfo.Environment.Remove(HiddenVariable);
        }

        var existing = startInfo.Environment.TryGetValue(StartupHooksVariable, out var value) ? value : null;
        startInfo.Environment[StartupHooksVariable] = string.IsNullOrEmpty(existing) ? AssemblyPath : existing + Path.PathSeparator + AssemblyPath;
        return true;
    }

    /// <summary>
    /// Asks the Companion in <paramref name="processId"/> to write pending binding values of the
    /// element (WinForms) or of its window (WPF) to their source.
    /// </summary>
    public static (CommitResult Result, int Count) CommitBindings(int processId, nint window, nint element, int timeoutMs)
    {
        var answer = Send(processId, string.Create(CultureInfo.InvariantCulture, $"commit {(long)window} {(long)element}"), timeoutMs, timeoutMs);
        return answer switch
        {
            null => (CommitResult.Unreachable, 0),
            "unsupported" => (CommitResult.Unsupported, 0),
            _ when TryParseCount(answer, out var count) => (CommitResult.Committed, count),
            _ => (CommitResult.Unreachable, 0),
        };
    }

    /// <summary>
    /// Asks the Companion to run <paramref name="action"/> on the element. Returns its answer (starting
    /// with <c>ok</c>, <c>notfound</c>, <c>unsupported</c> or <c>error</c>), or null when no Companion
    /// accepted the connection, in which case nothing was run.
    /// </summary>
    /// <remarks>
    /// Once the request is sent this waits for the answer without a timeout: the action may have opened
    /// a modal dialog, and falling back to UI Automation would run it twice. The session's own timeout
    /// for blocking calls abandons the wait instead.
    /// </remarks>
    public static string? Act(int processId, string action, string runtimeId, nint element, string? argument, int connectTimeoutMs)
    {
        var encoded = argument is null ? "-" : Convert.ToBase64String(Encoding.UTF8.GetBytes(argument));
        return Send(processId, string.Create(CultureInfo.InvariantCulture, $"act {action} {runtimeId} {(long)element} {encoded}"), connectTimeoutMs, Timeout.Infinite);
    }

    /// <summary>Shows or hides again a window of a hidden app, around a Foreground Action.</summary>
    public static bool SetRevealed(int processId, nint window, bool revealed, int timeoutMs) =>
        Send(processId, string.Create(CultureInfo.InvariantCulture, $"{(revealed ? "reveal" : "conceal")} {(long)window}"), timeoutMs, timeoutMs) == "ok";

    /// <summary>Parses the count of an <c>ok &lt;count&gt;</c> answer.</summary>
    public static bool TryParseCount(string answer, out int count)
    {
        count = 0;
        return answer.StartsWith("ok ", StringComparison.Ordinal)
            && int.TryParse(answer.AsSpan(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
    }

    private static string? Send(int processId, string request, int connectTimeoutMs, int answerTimeoutMs)
    {
        NamedPipeClientStream pipe;
        try
        {
            pipe = new NamedPipeClientStream(".", PipeName(processId), PipeDirection.InOut, PipeOptions.CurrentUserOnly | PipeOptions.Asynchronous);
            pipe.Connect(connectTimeoutMs);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                writer.WriteLine(request);
                var answer = reader.ReadLineAsync();
                return answer.Wait(answerTimeoutMs) ? answer.Result ?? "error closed" : "error timeout";
            }
            catch (Exception ex) when (ex is IOException or AggregateException)
            {
                return "error " + ex.GetType().Name;
            }
        }
    }

    private static string? Locate()
    {
        foreach (var directory in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(typeof(CompanionClient).Assembly.Location) })
        {
            if (!string.IsNullOrEmpty(directory) && Path.Combine(directory, AssemblyFileName) is var path && File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }
}
