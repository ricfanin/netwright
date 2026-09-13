using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Interop.UIAutomationClient;
using Netwright.Engine.Diagnostics;
using Netwright.Engine.Model;
using Netwright.Engine.Refs;
using Netwright.Engine.Selectors;
using Netwright.Engine.Snapshots;
using Netwright.Engine.Uia;

namespace Netwright.Engine.Session;

/// <summary>
/// A session with at most one Target App. Every operation is serialized, because UI Automation
/// state (the captured tree, Refs, focus) must not be observed half-way through another action.
/// </summary>
public sealed partial class DesktopSession : IAsyncDisposable
{
    private static readonly Lazy<bool> DpiAwareness = new(() => NativeMethods.SetProcessDpiAwarenessContext(NativeMethods.PerMonitorAwareV2));

    private readonly SessionOptions _options;
    private readonly IUIAutomation _uia;
    private readonly UiaTreeReader _reader;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AppOutput _output = new();
    private TargetApp? _app;
    private DebugOutputCapture? _debugCapture;
    private string? _debugWarning;
    private Task<string>? _pendingNotice;
    private bool _disposed;

    public DesktopSession(SessionOptions? options = null)
    {
        _ = DpiAwareness.Value;
        _options = options ?? new SessionOptions();
        // CUIAutomation8 is the Windows 8+ implementation (better WPF/WinUI support and timeouts).
        _uia = new CUIAutomation8();
        _reader = new UiaTreeReader(_uia);
    }

    public SessionOptions Options => _options;

    public RefRegistry Refs { get; } = new();

    public SessionRecorder Recorder { get; } = new();

    public bool HasApp => _app is { HasExited: false };

    internal UiaTreeReader Reader => _reader;

    internal TargetApp? CurrentApp => _app;

    // ---------------------------------------------------------------- lifecycle

    public Task<AppStatus> LaunchAsync(LaunchRequest request, CancellationToken cancellationToken = default) =>
        ExclusiveAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            string fileName;
            string arguments;
            string allowPath;

            if (!string.IsNullOrWhiteSpace(request.Project))
            {
                // Building runs the project's MSBuild logic, so the allow list must be checked before
                // the build, against the executable the project is expected to produce.
                EnsureAllowed(System.IO.Path.ChangeExtension(System.IO.Path.GetFullPath(request.Project), ".exe"));
                (fileName, arguments) = await ProjectBuilder.BuildAsync(_options.DotnetExecutable, request.Project, request.Configuration, request.Framework, cancellationToken).ConfigureAwait(false);
                if (request.Arguments.Count > 0)
                {
                    arguments = (arguments + " " + JoinArguments(request.Arguments)).Trim();
                }

                allowPath = Path.GetFileNameWithoutExtension(fileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
                    ? arguments.Trim('"').Split('"')[0]
                    : fileName;
            }
            else if (!string.IsNullOrWhiteSpace(request.Path))
            {
                fileName = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(request.Path));
                if (!File.Exists(fileName))
                {
                    throw new NetwrightException(ErrorCodes.LaunchFailed, $"Executable not found: {fileName}", "Check the path, or pass project=<.csproj> to build and launch.");
                }

                arguments = JoinArguments(request.Arguments);
                allowPath = fileName;
            }
            else
            {
                throw NetwrightException.InvalidArgument("Launch needs either path (an .exe) or project (a .csproj).");
            }

            EnsureAllowed(allowPath);
            await CloseCurrentForReplacementAsync().ConfigureAwait(false);

            var startInfo = new ProcessStartInfo(fileName, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = request.WorkingDirectory ?? System.IO.Path.GetDirectoryName(allowPath) ?? Environment.CurrentDirectory,
            };

            Process process;
            try
            {
                process = Process.Start(startInfo) ?? throw new NetwrightException(ErrorCodes.LaunchFailed, $"Windows did not start {fileName}.");
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new NetwrightException(ErrorCodes.LaunchFailed, $"Could not start {fileName}: {ex.Message}", null, ex);
            }

            var app = new TargetApp(process, allowPath, launchedBySession: true);
            process.EnableRaisingEvents = true;
            process.OutputDataReceived += (_, e) => { if (e.Data is not null) _output.Add(OutputStreams.Stdout, e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) _output.Add(OutputStreams.Stderr, e.Data); };
            process.Exited += (_, _) => OnProcessExited(app);
            SetApp(app);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            Recorder.Clear();
            Recorder.Record("launch", null,
                ("path", request.Path),
                ("project", request.Project),
                ("framework", request.Framework),
                ("arguments", request.Arguments.Count > 0 ? string.Join(Export.TestExporter.ArgumentSeparator, request.Arguments) : null));

            await WaitForFirstWindowAsync(app, request.TimeoutMs, cancellationToken).ConfigureAwait(false);
            return BuildStatus(app);
        }, cancellationToken);

    public Task<AppStatus> AttachAsync(AttachRequest request, CancellationToken cancellationToken = default) =>
        ExclusiveAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            var process = FindProcess(request);
            var path = TryGetExecutablePath(process);
            if (path is null && _options.AllowedApps.Count > 0)
            {
                // A process name is chosen by whoever named the executable; never let it satisfy the allow list.
                throw new NetwrightException(
                    ErrorCodes.NotAllowed,
                    $"Cannot read the executable path of {process.ProcessName} (pid {process.Id}), so it cannot be checked against the allowed apps list.",
                    "The process may be elevated; run Netwright at the same integrity level.");
            }

            path ??= process.ProcessName + ".exe";
            EnsureAllowed(path);

            if (_app is { HasExited: false } current && current.RootProcessId == process.Id)
            {
                return BuildStatus(current);
            }

            await CloseCurrentForReplacementAsync().ConfigureAwait(false);
            var app = new TargetApp(process, path, launchedBySession: false);
            try
            {
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => OnProcessExited(app);
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // Exit notifications are unavailable for processes we cannot open; exits are still detected on the next call.
            }

            SetApp(app);
            Recorder.Clear();
            Recorder.Record("attach", null, ("process", process.ProcessName));
            await WaitForFirstWindowAsync(app, request.TimeoutMs, cancellationToken).ConfigureAwait(false);
            return BuildStatus(app);
        }, cancellationToken);

    /// <summary>Closes the Target App gracefully, or kills it when <paramref name="force"/> is set.</summary>
    public Task<string> CloseAsync(bool force, CancellationToken cancellationToken = default) =>
        ExclusiveAsync(async () =>
        {
            var app = _app ?? throw NetwrightException.NoApp();
            app.CloseRequested = true;
            if (app.HasExited)
            {
                ClearApp();
                return "The Target App had already exited.";
            }

            if (!force)
            {
                try
                {
                    app.Process.CloseMainWindow();
                }
                catch (InvalidOperationException)
                {
                }

                if (await WaitForExitAsync(app, 5000).ConfigureAwait(false))
                {
                    ClearApp();
                    return $"Closed {app.ExecutableName}.";
                }

                app.CloseRequested = false;
                throw new NetwrightException(
                    ErrorCodes.Timeout,
                    $"{app.ExecutableName} is still running 5 s after being asked to close; it may be asking for confirmation.",
                    "Take a desktop_snapshot to answer the prompt, or close with force=true to kill it.");
            }

            try
            {
                app.Process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await WaitForExitAsync(app, 5000).ConfigureAwait(false);
            ClearApp();
            return $"Killed {app.ExecutableName}.";
        }, cancellationToken);

    public Task<AppStatus> StatusAsync(CancellationToken cancellationToken = default) =>
        ExclusiveAsync(() => Task.FromResult(BuildStatus(RequireApp())), cancellationToken);

    /// <summary>
    /// A Crash Report (or other unsolicited notice) that has not been shown to the Agent yet.
    /// Callers attach it to the next tool response.
    /// </summary>
    public async Task<string?> TakePendingNoticeAsync()
    {
        var notice = Interlocked.Exchange(ref _pendingNotice, null);
        if (notice is null)
        {
            return null;
        }

        var completed = await Task.WhenAny(notice, Task.Delay(5000)).ConfigureAwait(false);
        return completed == notice
            ? await notice.ConfigureAwait(false)
            : "! Target App exited unexpectedly (crash details are still being collected; see desktop_logs).";
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_app is { LaunchedBySession: true, HasExited: false } app)
            {
                app.CloseRequested = true;
                try
                {
                    app.Process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            }

            _app?.Dispose();
            _debugCapture?.Dispose();
            Marshal.FinalReleaseComObject(_uia);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    // ---------------------------------------------------------------- helpers

    private async Task<T> ExclusiveAsync<T>(Func<Task<T>> body, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await body().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private TargetApp RequireApp()
    {
        var app = _app ?? throw NetwrightException.NoApp();
        if (app.HasExited)
        {
            throw new NetwrightException(ErrorCodes.AppExited, $"The Target App ({app.ExecutableName}) has exited.", "Launch or attach again.");
        }

        return app;
    }

    private UiTree Capture(bool includeOffscreen = false)
    {
        var app = RequireApp();
        try
        {
            return _reader.Capture(app.ProcessIds, CaptureDetail.Full, includeOffscreen);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80131505))
        {
            throw new NetwrightException(ErrorCodes.AppNotResponding, $"{app.ExecutableName} is not responding to UI Automation.", "It may be busy or hung. Wait and retry.", ex);
        }
    }

    /// <summary>A cheaper capture for polling; nodes carry no live references, so never act on them.</summary>
    private UiTree CaptureLight(bool includeOffscreen = false)
    {
        var app = RequireApp();
        try
        {
            return _reader.Capture(app.ProcessIds, CaptureDetail.Light, includeOffscreen);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80131505))
        {
            throw new NetwrightException(ErrorCodes.AppNotResponding, $"{app.ExecutableName} is not responding to UI Automation.", "It may be busy or hung. Wait and retry.", ex);
        }
    }

    private SnapshotRenderer Renderer(SnapshotOptions? options = null) => new(Refs, options ?? _options.Snapshot);

    /// <summary>
    /// Resolves a Ref or Selector. Captures exclude offscreen elements for speed, so a miss is retried
    /// once against a capture that includes them; the tree the element was found in is returned.
    /// </summary>
    private (UiNode Node, UiTree Tree) Resolve(string target, UiTree tree)
    {
        try
        {
            return (ResolveNow(target, tree), tree);
        }
        catch (NetwrightException ex) when (!tree.IncludesOffscreen && ex.Code is ErrorCodes.ElementNotFound or ErrorCodes.StaleRef)
        {
            var everything = tree.IsLight ? CaptureLight(includeOffscreen: true) : Capture(includeOffscreen: true);
            try
            {
                return (ResolveNow(target, everything), everything);
            }
            catch (NetwrightException retry) when (retry.Code is ErrorCodes.ElementNotFound or ErrorCodes.StaleRef)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
                throw;
            }
        }
    }

    private UiNode ResolveNow(string target, UiTree tree)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            throw NetwrightException.InvalidArgument("A target is required: a Ref such as e12, or a Selector such as #btnSave.");
        }

        target = target.Trim();
        if (RefRegistry.LooksLikeRef(target))
        {
            return Refs.Resolve(target, tree);
        }

        var matches = SelectorMatcher.Match(SelectorParser.Parse(target), tree);
        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count == 0)
        {
            throw new NetwrightException(
                ErrorCodes.ElementNotFound,
                $"No element matches '{target}'.",
                "Take a desktop_snapshot to see what is on screen. " + SelectorParser.Syntax);
        }

        var renderer = Renderer();
        var candidates = string.Join(Environment.NewLine, matches.Take(6).Select(m => "  " + renderer.RenderLine(m)));
        throw new NetwrightException(
            ErrorCodes.AmbiguousSelector,
            $"'{target}' matches {matches.Count} elements:{Environment.NewLine}{candidates}",
            "Use one of these Refs, add :nth(n), or scope the selector (parent >> child).");
    }

    private static UiNode MainWindow(UiTree tree, TargetApp app)
    {
        if (tree.Windows.Count == 0)
        {
            throw new NetwrightException(ErrorCodes.ElementNotFound, $"{app.ExecutableName} has no open windows.");
        }

        nint mainHandle = 0;
        try
        {
            app.Process.Refresh();
            mainHandle = app.Process.MainWindowHandle;
        }
        catch (InvalidOperationException)
        {
        }

        return tree.Windows.FirstOrDefault(w => w.WindowHandle == mainHandle && mainHandle != 0) ?? tree.Windows[0];
    }

    private AppStatus BuildStatus(TargetApp app)
    {
        var tree = _reader.Capture(app.ProcessIds);
        var renderer = Renderer();
        var lines = tree.Windows.Select(w => renderer.RenderLine(w)).ToList();
        var framework = tree.Windows.Select(LiveFrameworkId).FirstOrDefault(f => f.Length > 0) switch
        {
            "WPF" => "WPF",
            "WinForm" => "WinForms",
            "XAML" or "DirectUI" => "WinUI/XAML",
            "Win32" => "Win32",
            null or "" => "unknown",
            var other => other,
        };

        return new AppStatus(app.RootProcessId, app.ExecutablePath, app.LaunchedBySession, framework, lines, _debugWarning);
    }

    private static string LiveFrameworkId(UiNode node)
    {
        try
        {
            return node.Native?.CurrentFrameworkId ?? "";
        }
        catch (COMException)
        {
            return "";
        }
    }

    private static string LiveClassName(UiNode node)
    {
        try
        {
            return node.Native?.CurrentClassName ?? "";
        }
        catch (COMException)
        {
            return "";
        }
    }

    private void SetApp(TargetApp app)
    {
        _app?.Dispose();
        _app = app;
        Refs.Clear();
        _output.Clear();
        _pendingNotice = null;
        _debugCapture ??= DebugOutputCapture.TryStart(pid => _app?.Owns(pid) == true, (_, line) => _output.Add(OutputStreams.Debug, line), out _debugWarning);
    }

    private void ClearApp()
    {
        _app?.Dispose();
        _app = null;
        Refs.Clear();
    }

    private async Task CloseCurrentForReplacementAsync()
    {
        if (_app is not { } current)
        {
            return;
        }

        current.CloseRequested = true;
        if (current.LaunchedBySession && !current.HasExited)
        {
            try
            {
                current.Process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }

            await WaitForExitAsync(current, 5000).ConfigureAwait(false);
        }

        ClearApp();
    }

    private void OnProcessExited(TargetApp app)
    {
        if (app.CloseRequested || !ReferenceEquals(app, _app))
        {
            return;
        }

        _pendingNotice = Task.Run(async () =>
        {
            int? exitCode = null;
            try
            {
                app.Process.WaitForExit(2000); // flush redirected stderr
                exitCode = app.Process.ExitCode;
            }
            catch (InvalidOperationException)
            {
            }

            return await CrashReporter.DescribeAsync(app.ExecutableName, exitCode, _output.Tail(OutputStreams.Stderr, 40), CancellationToken.None).ConfigureAwait(false);
        });
    }

    private async Task WaitForFirstWindowAsync(TargetApp app, int timeoutMs, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            if (app.HasExited)
            {
                app.CloseRequested = true;
                int? exitCode = null;
                try
                {
                    app.Process.WaitForExit(2000);
                    exitCode = app.Process.ExitCode;
                }
                catch (InvalidOperationException)
                {
                }

                var report = await CrashReporter.DescribeAsync(app.ExecutableName, exitCode, _output.Tail(OutputStreams.Stderr, 40), cancellationToken).ConfigureAwait(false);
                ClearApp();
                throw new NetwrightException(ErrorCodes.LaunchFailed, $"{app.ExecutableName} exited before showing a window.{Environment.NewLine}{report}");
            }

            if (UiaTreeReader.TopLevelWindowHandles(app.ProcessIds).Count > 0)
            {
                return;
            }

            if (stopwatch.ElapsedMilliseconds > timeoutMs)
            {
                throw new NetwrightException(
                    ErrorCodes.Timeout,
                    $"{app.ExecutableName} (pid {app.RootProcessId}) is running but showed no window within {timeoutMs} ms.",
                    "It stays attached; call desktop_snapshot later, or check desktop_logs.");
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<bool> WaitForExitAsync(TargetApp app, int timeoutMs)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await app.Process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            return app.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private void EnsureAllowed(string executablePath)
    {
        if (_options.AllowedApps.Count == 0)
        {
            return;
        }

        var name = System.IO.Path.GetFileName(executablePath);
        foreach (var pattern in _options.AllowedApps)
        {
            var regex = "^" + Regex.Escape(pattern.Trim()).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
            if (Regex.IsMatch(executablePath, regex, RegexOptions.IgnoreCase) || Regex.IsMatch(name, regex, RegexOptions.IgnoreCase) ||
                Regex.IsMatch(System.IO.Path.GetFileNameWithoutExtension(name), regex, RegexOptions.IgnoreCase))
            {
                return;
            }
        }

        throw new NetwrightException(
            ErrorCodes.NotAllowed,
            $"{executablePath} is not in the allowed apps list.",
            $"The server was started with --allow {string.Join(" --allow ", _options.AllowedApps)}. Ask the User to add this app.");
    }

    private static Process FindProcess(AttachRequest request)
    {
        if (request.ProcessId is { } pid)
        {
            try
            {
                return Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                throw new NetwrightException(ErrorCodes.NoApp, $"No process with id {pid} is running.");
            }
        }

        if (!string.IsNullOrWhiteSpace(request.ProcessName))
        {
            var name = request.ProcessName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? request.ProcessName[..^4] : request.ProcessName;
            var processes = Process.GetProcessesByName(name);
            return processes.OrderByDescending(p => SafeMainWindow(p) != 0).FirstOrDefault()
                ?? throw new NetwrightException(ErrorCodes.NoApp, $"No process named '{name}' is running.");
        }

        if (!string.IsNullOrWhiteSpace(request.WindowTitle))
        {
            return Process.GetProcesses().FirstOrDefault(p => SafeTitle(p).Contains(request.WindowTitle, StringComparison.OrdinalIgnoreCase))
                ?? throw new NetwrightException(ErrorCodes.NoApp, $"No window title contains '{request.WindowTitle}'.");
        }

        throw NetwrightException.InvalidArgument("Attach needs process_id, process_name or window_title.");
    }

    private static nint SafeMainWindow(Process process)
    {
        try
        {
            return process.MainWindowHandle;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    private static string SafeTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch (InvalidOperationException)
        {
            return "";
        }
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    internal static string JoinArguments(IEnumerable<string> arguments)
    {
        var sb = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0)
            {
                sb.Append(argument);
            }
            else
            {
                sb.Append('"').Append(argument.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            }
        }

        return sb.ToString();
    }
}
