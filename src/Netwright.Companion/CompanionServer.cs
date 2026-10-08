using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Netwright.Companion;

/// <summary>
/// Serves the Engine's requests over a named pipe that only the current user can open, one request
/// per connection. Connections are served concurrently, because a request can block while the action
/// it ran shows a modal dialog that the next request has to close. Every failure is swallowed: the
/// Companion must never change how the Target App behaves or take it down.
/// </summary>
internal static class CompanionServer
{
    private const int MaxConsecutiveFailures = 10;

    /// <summary>Must match <c>Netwright.Engine.Companion.CompanionClient.PipeName</c>.</summary>
    public static string PipeName(int processId) => "netwright-companion-" + processId.ToString(CultureInfo.InvariantCulture);

    public static void Start()
    {
        try
        {
            new Thread(Serve) { IsBackground = true, Name = "Netwright Companion" }.Start();
        }
        catch (Exception)
        {
        }
    }

    /// <summary>
    /// Handles one request line:
    /// <list type="bullet">
    /// <item><c>commit &lt;window&gt; &lt;element&gt;</c>: write pending binding values of the element
    /// (WinForms) or of the window (WPF) to their source. Answers <c>ok &lt;count&gt;</c>.</item>
    /// <item><c>act &lt;action&gt; &lt;runtime id&gt; &lt;element&gt; &lt;base64 argument or -&gt;</c>: run
    /// a pattern on the element. Answers <c>ok</c> (<c>ok &lt;count&gt;</c> for setvalue, with the
    /// bindings it committed), <c>notfound</c> or <c>unsupported</c>.</item>
    /// <item><c>reveal &lt;window&gt;</c> / <c>conceal &lt;window&gt;</c>: show or hide a window of a hidden
    /// app around a Foreground Action. Answers <c>ok</c>.</item>
    /// </list>
    /// Anything else, or a failure, answers <c>error</c>.
    /// </summary>
    public static string Handle(string? request)
    {
        var parts = request?.Split(' ') ?? [];
        try
        {
            return parts switch
            {
                ["commit", var window, var element] when TryHandle(window, out var w) && TryHandle(element, out var e) => Commit(w, e),
                ["act", var action, var runtimeId, var element, var argument] when TryHandle(element, out var e) => Act(action, runtimeId, e, Decode(argument)),
                ["reveal" or "conceal", var window] when TryHandle(window, out var w) => HiddenWindows.SetHidden(w, parts[0] == "conceal") ? "ok" : "error hide",
                _ => "error bad request",
            };
        }
        catch (Exception ex)
        {
            return "error " + ex.GetType().Name;
        }
    }

    private static string Commit(nint window, nint element)
    {
        int? committed = null;
        if (IsLoaded("System.Windows.Forms"))
        {
            committed = WinFormsBindings.Commit(element);
        }

        if (committed is null && IsLoaded("PresentationFramework"))
        {
            committed = WpfBindings.Commit(window);
        }

        return committed is { } count ? "ok " + count.ToString(CultureInfo.InvariantCulture) : "unsupported";
    }

    private static string Act(string action, string runtimeId, nint element, string? argument)
    {
        string? answer = null;
        if (IsLoaded("System.Windows.Forms"))
        {
            answer = WinFormsActions.Perform(action, element, argument);
        }

        if (answer is null && IsLoaded("PresentationFramework"))
        {
            answer = WpfActions.Perform(action, runtimeId, argument);
        }

        return answer ?? "unsupported";
    }

    private static void Serve()
    {
        var name = PipeName(Environment.ProcessId);
        var failures = 0;
        while (failures < MaxConsecutiveFailures)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                pipe.WaitForConnection();
                var connection = pipe;
                pipe = null;
                ThreadPool.UnsafeQueueUserWorkItem(_ => Answer(connection), null);
                failures = 0;
            }
            catch (Exception)
            {
                pipe?.Dispose();
                failures++;
                Thread.Sleep(100);
            }
        }
    }

    private static void Answer(NamedPipeServerStream pipe)
    {
        try
        {
            using (pipe)
            {
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                writer.WriteLine(Handle(reader.ReadLine()));
            }
        }
        catch (Exception)
        {
        }
    }

    private static bool TryHandle(string text, out nint handle)
    {
        var parsed = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value);
        handle = (nint)value;
        return parsed;
    }

    private static string? Decode(string argument) =>
        argument == "-" ? null : Encoding.UTF8.GetString(Convert.FromBase64String(argument));

    // Checked before touching a UI stack, so a WinForms app never loads WPF and vice versa.
    private static bool IsLoaded(string assemblyName) =>
        AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == assemblyName);
}
