using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Netwright.Engine;
using Netwright.Engine.Session;

namespace Netwright;

/// <summary>Machine-readable summary returned as structured content next to the text the Agent reads.</summary>
public sealed record ToolOutcome(bool Ok, string? Code = null, bool? Settled = null, int? Changes = null, long? ElapsedMs = null);

/// <summary>
/// The MCP tools. Every tool answers with compact text for the model; failures come back as
/// <c>isError</c> results with a code and a hint instead of exceptions. Tool and parameter
/// descriptions are kept short on purpose: they are sent to the model on every turn.
/// </summary>
[McpServerToolType]
public sealed class DesktopTools(DesktopSession session)
{
    private const string Target = "Ref or Selector";

    [McpServerTool(Name = "desktop_app", Title = "Target App", Destructive = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("Launch (path to .exe, or project to build a .csproj), attach (process: pid, name or window title), close, or status of the Target App.")]
    public Task<CallToolResult> App(
        [Description("launch|attach|close|status")] string action,
        string? path = null,
        string? project = null,
        string[]? args = null,
        [Description("For multi-targeted projects")] string? framework = null,
        string? process = null,
        [Description("Kill on close")] bool force = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            switch (Normalize(action))
            {
                case "launch":
                    var launched = await session.LaunchAsync(new LaunchRequest { Path = path, Project = project, Arguments = args ?? [], Framework = framework }, cancellationToken);
                    return Reply.Text(StatusText(launched, "Launched"));
                case "attach":
                    var attached = await session.AttachAsync(AttachRequestFor(process), cancellationToken);
                    return Reply.Text(StatusText(attached, "Attached to"));
                case "close":
                    return Reply.Text(await session.CloseAsync(force, cancellationToken));
                case "status":
                    return Reply.Text(StatusText(await session.StatusAsync(cancellationToken), "Target App:"));
                default:
                    throw NetwrightException.InvalidArgument($"Unknown action '{action}'.", "Use launch, attach, close or status.");
            }
        });

    [McpServerTool(Name = "desktop_snapshot", Title = "Snapshot", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("The Target App UI as text with Refs ([e12]), filtered to what matters.")]
    public Task<CallToolResult> Snapshot(
        [Description("Only this subtree")] string? root = null,
        [Description("Every element, unfiltered")] bool full = false,
        bool include_offscreen = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            var defaults = session.Options.Snapshot;
            var options = defaults with
            {
                Full = full,
                IncludeOffscreen = include_offscreen,
                MaxLines = full ? Math.Max(defaults.MaxLines, 2000) : defaults.MaxLines,
                MaxItemsPerContainer = full ? int.MaxValue : defaults.MaxItemsPerContainer,
            };
            return Reply.Text((await session.SnapshotAsync(root, options, cancellationToken)).Text);
        });

    [McpServerTool(Name = "desktop_find", Title = "Find", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("Elements matching a Selector, with Refs. Selectors: #automationId, role \"name\", role ~\"partial\", a >> b, :nth(2).")]
    public Task<CallToolResult> Find(string selector, CancellationToken cancellationToken = default) =>
        RunAsync(async () => Reply.Text(await session.FindAsync(selector, 30, cancellationToken)));

    [McpServerTool(Name = "desktop_inspect", Title = "Inspect", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("Properties, patterns and a stable Selector of one element.")]
    public Task<CallToolResult> Inspect([Description(Target)] string target, CancellationToken cancellationToken = default) =>
        RunAsync(async () => Reply.Text(await session.InspectAsync(target, cancellationToken)));

    [McpServerTool(Name = "desktop_click", Title = "Click", Destructive = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("Click an element in the background. Right/double clicks and elements without click support need foreground=true.")]
    public Task<CallToolResult> Click(
        [Description(Target)] string target,
        [Description("left|right|middle")] string button = "left",
        bool double_click = false,
        [Description("Real mouse; focus restored after")] bool foreground = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => Reply.Action(await session.ClickAsync(target, new ClickRequest
        {
            Button = ParseEnum(button, MouseButtonKind.Left, nameof(button)),
            ClickCount = double_click ? 2 : 1,
            Foreground = foreground,
        }, cancellationToken)));

    [McpServerTool(Name = "desktop_type", Title = "Type", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("Set the text of an input in the background. foreground=true types real keys (needed for submit).")]
    public Task<CallToolResult> Type(
        [Description(Target)] string target,
        string text,
        [Description("Replace existing text")] bool clear = true,
        [Description("Press Enter after")] bool submit = false,
        bool foreground = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => Reply.Action(await session.TypeAsync(target, new TypeRequest { Text = text ?? "", Clear = clear, Submit = submit, Foreground = foreground }, cancellationToken)));

    [McpServerTool(Name = "desktop_set_state", Title = "Set state", Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("Set one of: checked, selected, expanded, value (slider or input), item (select a child of a list, combo, tabs or tree by name).")]
    public Task<CallToolResult> SetState(
        [Description(Target)] string target,
        bool? @checked = null,
        bool? selected = null,
        bool? expanded = null,
        string? value = null,
        string? item = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => Reply.Action(await session.SetStateAsync(target, new SetStateRequest
        {
            Checked = @checked,
            Selected = selected,
            Expanded = expanded,
            Value = value,
            Item = item,
        }, cancellationToken)));

    [McpServerTool(Name = "desktop_press_key", Title = "Press key", Destructive = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("Press keys, e.g. Enter, Ctrl+S, \"Tab Tab Enter\". Needs foreground=true.")]
    public Task<CallToolResult> PressKey(
        string keys,
        [Description("Focus first")] string? target = null,
        bool foreground = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => Reply.Action(await session.PressKeyAsync(target, keys, foreground, cancellationToken)));

    [McpServerTool(Name = "desktop_scroll", Title = "Scroll", Destructive = false, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("Scroll the target's container by direction or to an edge, or scroll the target into view.")]
    public Task<CallToolResult> Scroll(
        [Description(Target)] string target,
        [Description("up|down|left|right")] string? direction = null,
        [Description("small|page")] string amount = "page",
        [Description("top|bottom|start|end")] string? to = null,
        bool into_view = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => Reply.Action(await session.ScrollAsync(target, new ScrollRequest
        {
            Direction = direction is null ? null : ParseEnum(direction, ScrollDirection.Down, nameof(direction)),
            Amount = ParseEnum(amount, ScrollAmountKind.Page, nameof(amount)),
            To = to is null ? null : ParseEnum(to, ScrollEdge.Top, nameof(to)),
            IntoView = into_view,
        }, cancellationToken)));

    [McpServerTool(Name = "desktop_window", Title = "Window", Destructive = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("List or change Target App windows (default: main window). activate needs foreground=true.")]
    public Task<CallToolResult> Window(
        [Description("list|activate|minimize|maximize|restore|close")] string action,
        [Description("A window or an element in it")] string? target = null,
        bool foreground = false,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            var parsed = ParseEnum(action, WindowAction.List, nameof(action));
            return parsed == WindowAction.List
                ? Reply.Text(await session.ListWindowsAsync(cancellationToken))
                : Reply.Action(await session.WindowAsync(target, parsed, foreground, cancellationToken));
        });

    [McpServerTool(Name = "desktop_screenshot", Title = "Screenshot", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("Image of a window or element (default: main window), even when covered by other windows.")]
    public Task<CallToolResult> Screenshot(
        [Description(Target)] string? target = null,
        [Description("Longest side, px")] int max_size = 1280,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            var shot = await session.ScreenshotAsync(target, max_size, cancellationToken);
            return Reply.Image(shot.Summary, shot.Image.Png);
        });

    [McpServerTool(Name = "desktop_wait", Title = "Wait", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("Wait for an element state, or for text to appear (or go with state=gone). Reports what changed meanwhile.")]
    public Task<CallToolResult> Wait(
        [Description(Target)] string? target = null,
        [Description("visible|hidden|enabled|disabled|exists|gone")] string state = "visible",
        string? text = null,
        int timeout_ms = 10000,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => Reply.Action(await session.WaitAsync(new WaitRequest
        {
            Target = target,
            Text = text,
            State = ParseEnum(state, ElementState.Visible, nameof(state)),
            TimeoutMs = Math.Clamp(timeout_ms, 100, 600000),
        }, cancellationToken)));

    [McpServerTool(Name = "desktop_expect", Title = "Expect", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("Verify an element, retrying until timeout.")]
    public Task<CallToolResult> Expect(
        [Description(Target)] string target,
        [Description("exists|gone|visible|hidden|enabled|disabled|checked|unchecked|selected|not_selected|expanded|collapsed|focused|text|text_contains|value|count")] string assertion,
        [Description("For text, text_contains, value, count")] string? expected = null,
        int timeout_ms = 2000,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () => Reply.Text(await session.ExpectAsync(target, new ExpectRequest
        {
            Assertion = ParseEnum(assertion, Assertion.Exists, nameof(assertion)),
            Expected = expected,
            TimeoutMs = Math.Clamp(timeout_ms, 0, 600000),
        }, cancellationToken)));

    [McpServerTool(Name = "desktop_logs", Title = "App output", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("New Target App output: stdout/stderr (if launched by Netwright) and debug/trace lines.")]
    public Task<CallToolResult> Logs(
        [Description("stdout|stderr|debug")] string? stream = null,
        [Description("Include lines already returned")] bool all = false) =>
        RunAsync(() =>
        {
            var normalized = stream is null ? null : Normalize(stream);
            if (normalized is not (null or "stdout" or "stderr" or "debug"))
            {
                throw NetwrightException.InvalidArgument($"Unknown stream '{stream}'.", "Use stdout, stderr or debug.");
            }

            var logs = session.ReadLogs(normalized, 200, all);
            var sb = new StringBuilder();
            if (logs.Skipped > 0)
            {
                sb.Append(CultureInfo.InvariantCulture, $"({logs.Skipped} earlier lines skipped)").AppendLine();
            }

            foreach (var entry in logs.Entries)
            {
                sb.AppendLine(entry.ToString());
            }

            if (logs.Entries.Count == 0)
            {
                sb.AppendLine(all ? "No output captured." : "No new output.");
            }

            if (logs.Warning is not null)
            {
                sb.Append("note: ").AppendLine(logs.Warning);
            }

            return Task.FromResult(Reply.Text(sb.ToString().TrimEnd()));
        });

    [McpServerTool(Name = "desktop_export_test", Title = "Export test", ReadOnly = true, OpenWorld = false, UseStructuredContent = true, OutputSchemaType = typeof(ToolOutcome))]
    [Description("C# test (Netwright.Testing) replaying this session's successful actions and expectations.")]
    public Task<CallToolResult> ExportTest(
        [Description("Test name, e.g. \"checkout succeeds\"")] string name = "recorded flow",
        [Description("xunit|nunit|mstest")] string framework = "xunit",
        [Description("C# namespace")] string? @namespace = null) =>
        RunAsync(() =>
        {
            var steps = session.Recorder.Steps;
            var code = Engine.Export.TestExporter.Export(steps, name, ParseEnum(framework, Engine.Export.TestFramework.XUnit, nameof(framework)), @namespace ?? "UiTests");
            var text = string.Create(CultureInfo.InvariantCulture,
                $"Test Export ({steps.Count - 1} steps). Save it in a test project that references Netwright.Testing (dotnet add package Netwright.Testing).{Environment.NewLine}```csharp{Environment.NewLine}{code}```");
            return Task.FromResult(Reply.Text(text));
        });

    // ---------------------------------------------------------------- plumbing

    private static readonly JsonSerializerOptions OutcomeJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private async Task<CallToolResult> RunAsync(Func<Task<Reply>> body)
    {
        Reply reply;
        try
        {
            reply = await body().ConfigureAwait(false);
        }
        catch (NetwrightException ex)
        {
            reply = Reply.Error(ex.Code, ex.Message, ex.Hint);
        }
        catch (OperationCanceledException)
        {
            reply = Reply.Error(ErrorCodes.Timeout, "The operation was cancelled.", null);
        }
        catch (Exception ex)
        {
            reply = Reply.Error("INTERNAL", $"Unexpected {ex.GetType().Name}: {ex.Message}", "This is a Netwright bug; please report it.");
        }

        var notice = await session.TakePendingNoticeAsync().ConfigureAwait(false);
        var text = notice is null ? reply.Body : notice + Environment.NewLine + reply.Body;

        var content = new List<ContentBlock> { new TextContentBlock { Text = text } };
        if (reply.ImagePng is { } png)
        {
            content.Add(ImageContentBlock.FromBytes(png, "image/png"));
        }

        return new CallToolResult
        {
            Content = content,
            IsError = reply.Outcome.Ok ? null : true,
            StructuredContent = JsonSerializer.SerializeToElement(reply.Outcome, OutcomeJson),
        };
    }

    internal static AttachRequest AttachRequestFor(string? process)
    {
        if (string.IsNullOrWhiteSpace(process))
        {
            throw NetwrightException.InvalidArgument("Attach needs process: a process id, a process name, or part of a window title.");
        }

        process = process.Trim();
        if (int.TryParse(process, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
        {
            return new AttachRequest { ProcessId = pid };
        }

        var name = process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? process[..^4] : process;
        var byName = Process.GetProcessesByName(name);
        var exists = byName.Length > 0;
        foreach (var p in byName)
        {
            p.Dispose();
        }

        return exists ? new AttachRequest { ProcessName = name } : new AttachRequest { WindowTitle = process };
    }

    private static string StatusText(AppStatus status, string verb)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"{verb} {Path.GetFileName(status.ExecutablePath)} (pid {status.ProcessId}, {status.Framework}{(status.LaunchedBySession ? ", launched by Netwright" : ", attached")})");
        foreach (var line in status.WindowLines)
        {
            sb.AppendLine().Append("- ").Append(line);
        }

        if (status.WindowLines.Count == 0)
        {
            sb.AppendLine().Append("No windows yet.");
        }

        if (status.DebugCaptureWarning is not null)
        {
            sb.AppendLine().Append("note: ").Append(status.DebugCaptureWarning);
        }

        return sb.ToString();
    }

    private static string Normalize(string value) =>
        value.Trim().Replace("_", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

    internal static T ParseEnum<T>(string value, T fallback, string parameter)
        where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (Enum.TryParse<T>(Normalize(value), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        var allowed = string.Join(", ", Enum.GetNames<T>().Select(SnakeCase));
        throw NetwrightException.InvalidArgument($"Invalid {parameter} '{value}'.", $"Use one of: {allowed}.");
    }

    private static string SnakeCase(string name)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                sb.Append('_');
            }

            sb.Append(char.ToLowerInvariant(name[i]));
        }

        return sb.ToString();
    }

    private sealed record Reply(string Body, ToolOutcome Outcome, byte[]? ImagePng = null)
    {
        public static Reply Text(string text) => new(text, new ToolOutcome(true));

        public static Reply Image(string text, byte[] png) => new(text, new ToolOutcome(true), png);

        public static Reply Error(string code, string message, string? hint) =>
            new($"error {code}: {message}{(hint is null ? "" : Environment.NewLine + "hint: " + hint)}", new ToolOutcome(false, code));

        public static Reply Action(ActionOutcome outcome)
        {
            var sb = new StringBuilder(outcome.Summary);
            if (!outcome.Settled)
            {
                sb.Append(CultureInfo.InvariantCulture, $" (UI still changing after {outcome.ElapsedMs} ms)");
            }

            sb.AppendLine();
            sb.Append(outcome.Changes.IsEmpty ? "no visible changes" : outcome.Changes.Text);
            if (outcome.Note is not null)
            {
                sb.AppendLine().Append("note: ").Append(outcome.Note);
            }

            return new(sb.ToString(), new ToolOutcome(true, null, outcome.Settled, outcome.Changes.Changes, outcome.ElapsedMs));
        }
    }
}
