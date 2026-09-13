using Netwright.Engine.Diagnostics;
using Netwright.Engine.Session;

namespace Netwright.Testing;

/// <summary>
/// A running .NET desktop app under test. Every action waits for its target to be Actionable, runs
/// in the background without taking your focus, and waits for the UI to settle, exactly as it does
/// when an AI agent drives the app through Netwright.
/// </summary>
/// <example>
/// <code>
/// await using var app = await DesktopApp.LaunchAsync(@"bin\Debug\net8.0-windows\MyApp.exe");
/// await app.TypeAsync("#txtName", "Ada");
/// await app.ClickAsync("#btnSave");
/// await app.Expect("#lblStatus").ToContainTextAsync("Saved");
/// </code>
/// </example>
public sealed class DesktopApp : IAsyncDisposable
{
    private DesktopApp(DesktopSession session) => Session = session;

    /// <summary>The underlying Engine session, for anything this API does not cover.</summary>
    public DesktopSession Session { get; }

    public static Task<DesktopApp> LaunchAsync(string path, params string[] arguments) =>
        StartAsync(new LaunchRequest { Path = path, Arguments = arguments });

    /// <summary>Builds the project with the .NET SDK and launches the resulting app.</summary>
    public static Task<DesktopApp> LaunchProjectAsync(string project, string? framework = null, params string[] arguments) =>
        StartAsync(new LaunchRequest { Project = project, Framework = framework, Arguments = arguments, TimeoutMs = 120000 });

    /// <summary>Attaches to a running app by process id, process name, or part of a window title.</summary>
    public static async Task<DesktopApp> AttachAsync(string process, SessionOptions? options = null)
    {
        var session = new DesktopSession(options);
        try
        {
            var request = int.TryParse(process, out var pid)
                ? new AttachRequest { ProcessId = pid }
                : System.Diagnostics.Process.GetProcessesByName(process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? process[..^4] : process).Length > 0
                    ? new AttachRequest { ProcessName = process }
                    : new AttachRequest { WindowTitle = process };
            await session.AttachAsync(request).ConfigureAwait(false);
            return new DesktopApp(session);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<DesktopApp> StartAsync(LaunchRequest request, SessionOptions? options = null)
    {
        var session = new DesktopSession(options);
        try
        {
            await session.LaunchAsync(request).ConfigureAwait(false);
            return new DesktopApp(session);
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    // ---------------------------------------------------------------- actions

    public Task ClickAsync(string target, bool foreground = false) =>
        Session.ClickAsync(target, new ClickRequest { Foreground = foreground });

    public Task DoubleClickAsync(string target) =>
        Session.ClickAsync(target, new ClickRequest { ClickCount = 2, Foreground = true });

    public Task RightClickAsync(string target) =>
        Session.ClickAsync(target, new ClickRequest { Button = MouseButtonKind.Right, Foreground = true });

    public Task TypeAsync(string target, string text, bool clear = true, bool submit = false, bool foreground = false) =>
        Session.TypeAsync(target, new TypeRequest { Text = text, Clear = clear, Submit = submit, Foreground = foreground || submit });

    public Task CheckAsync(string target) => Session.SetStateAsync(target, new SetStateRequest { Checked = true });

    public Task UncheckAsync(string target) => Session.SetStateAsync(target, new SetStateRequest { Checked = false });

    /// <summary>Selects a child item by name in a list, combo box, tab control or tree.</summary>
    public Task SelectAsync(string target, string item) => Session.SetStateAsync(target, new SetStateRequest { Item = item });

    public Task SetSelectedAsync(string target, bool selected = true) => Session.SetStateAsync(target, new SetStateRequest { Selected = selected });

    public Task ExpandAsync(string target) => Session.SetStateAsync(target, new SetStateRequest { Expanded = true });

    public Task CollapseAsync(string target) => Session.SetStateAsync(target, new SetStateRequest { Expanded = false });

    /// <summary>Sets a slider (number) or an input (text) directly.</summary>
    public Task SetValueAsync(string target, string value) => Session.SetStateAsync(target, new SetStateRequest { Value = value });

    /// <summary>Presses keys such as <c>Enter</c>, <c>Ctrl+S</c> or <c>Tab Tab Enter</c>. Takes focus briefly.</summary>
    public Task PressKeyAsync(string keys, string? target = null) => Session.PressKeyAsync(target, keys, foreground: true);

    public Task ScrollAsync(string target, ScrollDirection direction, ScrollAmountKind amount = ScrollAmountKind.Page) =>
        Session.ScrollAsync(target, new ScrollRequest { Direction = direction, Amount = amount });

    public Task ScrollToAsync(string target, ScrollEdge edge) => Session.ScrollAsync(target, new ScrollRequest { To = edge });

    public Task ScrollIntoViewAsync(string target) => Session.ScrollAsync(target, new ScrollRequest { IntoView = true });

    public Task WindowAsync(WindowAction action, string? target = null) => Session.WindowAsync(target, action, foreground: action == WindowAction.Activate);

    // ---------------------------------------------------------------- waiting and verifying

    public Task WaitForTextAsync(string text, int timeoutMs = 10000) =>
        Session.WaitAsync(new WaitRequest { Text = text, TimeoutMs = timeoutMs });

    public Task WaitForTextGoneAsync(string text, int timeoutMs = 10000) =>
        Session.WaitAsync(new WaitRequest { Text = text, State = ElementState.Gone, TimeoutMs = timeoutMs });

    public Task WaitForAsync(string target, ElementState state = ElementState.Visible, int timeoutMs = 10000) =>
        Session.WaitAsync(new WaitRequest { Target = target, State = state, TimeoutMs = timeoutMs });

    /// <summary>Starts an expectation on an element; every check retries until it passes or times out.</summary>
    public Expectation Expect(string target, int timeoutMs = 5000) => new(Session, target, timeoutMs);

    // ---------------------------------------------------------------- observing

    public async Task<string> SnapshotAsync(string? root = null) => (await Session.SnapshotAsync(root).ConfigureAwait(false)).Text;

    public async Task<byte[]> ScreenshotAsync(string? target = null) => (await Session.ScreenshotAsync(target).ConfigureAwait(false)).Image.Png;

    /// <summary>App Output captured since the last call.</summary>
    public IReadOnlyList<OutputEntry> Output(bool includeAlreadyRead = false) => Session.ReadLogs(null, 2000, includeAlreadyRead).Entries;

    public async ValueTask DisposeAsync() => await Session.DisposeAsync().ConfigureAwait(false);
}
