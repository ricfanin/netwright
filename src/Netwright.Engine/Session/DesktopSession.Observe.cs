using System.Diagnostics;
using System.Globalization;
using System.Text;
using Netwright.Engine.Capture;
using Netwright.Engine.Diagnostics;
using Netwright.Engine.Model;
using Netwright.Engine.Refs;
using Netwright.Engine.Selectors;
using Netwright.Engine.Snapshots;
using Netwright.Engine.Uia;

namespace Netwright.Engine.Session;

public sealed partial class DesktopSession
{
    public Task<SnapshotOutcome> SnapshotAsync(string? root = null, SnapshotOptions? options = null, CancellationToken cancellationToken = default) =>
        ExclusiveAsync(() =>
        {
            var tree = Capture(includeOffscreen: (options ?? _options.Snapshot).IncludeOffscreen);
            if (tree.Windows.Count == 0)
            {
                return Task.FromResult(new SnapshotOutcome("The Target App has no open windows.", 0, 0));
            }

            var renderer = Renderer(options);
            IEnumerable<UiNode> roots = string.IsNullOrWhiteSpace(root) ? tree.Windows : [Resolve(root, tree).Node];
            var result = renderer.Render(roots);
            return Task.FromResult(new SnapshotOutcome(result.Text, result.Lines, tree.Windows.Count));
        }, cancellationToken);

    public Task<string> FindAsync(string selector, int max = 30, CancellationToken cancellationToken = default) =>
        ExclusiveAsync(() =>
        {
            var parsed = SelectorParser.Parse(selector);
            var matches = SelectorMatcher.Match(parsed, Capture());
            if (matches.Count == 0)
            {
                matches = SelectorMatcher.Match(parsed, Capture(includeOffscreen: true));
            }
            if (matches.Count == 0)
            {
                throw new NetwrightException(ErrorCodes.ElementNotFound, $"No element matches '{selector}'.", "Take a desktop_snapshot to see what is on screen.");
            }

            var renderer = Renderer();
            var sb = new StringBuilder();
            sb.Append(CultureInfo.InvariantCulture, $"{matches.Count} match{(matches.Count == 1 ? "" : "es")}:").AppendLine();
            foreach (var node in matches.Take(max))
            {
                sb.Append("- ").Append(renderer.RenderLine(node, withRef: true));
                if (node.IsOffscreen)
                {
                    sb.Append(" offscreen");
                }

                sb.AppendLine();
            }

            if (matches.Count > max)
            {
                sb.Append(CultureInfo.InvariantCulture, $"... {matches.Count - max} more").AppendLine();
            }

            return Task.FromResult(sb.ToString().TrimEnd());
        }, cancellationToken);

    public Task<string> InspectAsync(string target, CancellationToken cancellationToken = default) =>
        ExclusiveAsync(() =>
        {
            var (node, tree) = Resolve(target, Capture());
            var renderer = Renderer();
            var sb = new StringBuilder();
            sb.AppendLine(renderer.RenderLine(node));
            sb.Append("selector: ").AppendLine(SelectorBuilder.Build(node, tree).ToString());
            sb.Append("path: ").AppendLine(string.Join(" > ", node.Ancestors().Reverse().Where(renderer.IsReportable).Append(node).Select(Short)));
            var bounds = UiaTreeReader.LiveBounds(node);
            sb.Append(CultureInfo.InvariantCulture, $"automationId: {Show(node.AutomationId)} | class: {Show(LiveClassName(node))} | framework: {Show(LiveFrameworkId(node))}").AppendLine();
            sb.Append(CultureInfo.InvariantCulture, $"bounds: x={bounds.X} y={bounds.Y} w={bounds.Width} h={bounds.Height}{(node.IsOffscreen ? " (offscreen)" : "")}").AppendLine();
            sb.Append("patterns: ").AppendLine(string.Join(", ", PatternCalls.SupportedPatternNames(node)) is { Length: > 0 } p ? p : "none");
            if (node.Has(UiPatterns.RangeValue) && PatternCalls.RangeLimits(node) is var (minimum, maximum))
            {
                sb.Append(CultureInfo.InvariantCulture, $"range: {minimum} to {maximum}").AppendLine();
            }

            sb.Append(CultureInfo.InvariantCulture, $"enabled: {node.IsEnabled} | focused: {node.HasFocus}").AppendLine();
            if (node.Value is not null)
            {
                sb.Append("value: \"").Append(node.IsPassword ? "***" : SnapshotRenderer.Escape(node.Value)).AppendLine("\"");
            }

            sb.Append(CultureInfo.InvariantCulture, $"children: {node.Children.Count}");
            return Task.FromResult(sb.ToString());

            static string Show(string value) => value.Length == 0 ? "-" : value;
            string Short(UiNode n) => n.Name.Length > 0 ? $"{n.Role} \"{Truncate(n.Name, 30)}\"" : n.AutomationId.Length > 0 ? $"{n.Role} #{n.AutomationId}" : n.Role;
        }, cancellationToken);

    public Task<ScreenshotOutcome> ScreenshotAsync(string? target = null, int maxSize = 1280, CancellationToken cancellationToken = default) =>
        ExclusiveAsync(() =>
        {
            var app = RequireApp();
            var tree = Capture();
            var node = string.IsNullOrWhiteSpace(target) ? MainWindow(tree, app) : Resolve(target, tree).Node;
            var window = node.Window;
            var image = WindowCapture.Capture(UiaTreeReader.LiveWindowHandle(window), ReferenceEquals(node, window) ? null : UiaTreeReader.LiveBounds(node), Math.Clamp(maxSize, 200, 4096));
            var summary = string.Create(CultureInfo.InvariantCulture,
                $"Screenshot of {Renderer().RenderLine(node)}: {image.Width}x{image.Height}{(image.Width != image.SourceWidth ? $" (scaled from {image.SourceWidth}x{image.SourceHeight})" : "")}");
            return Task.FromResult(new ScreenshotOutcome(image, summary));
        }, cancellationToken);

    public Task<ActionOutcome> WaitAsync(WaitRequest request, CancellationToken cancellationToken = default) =>
        ExclusiveAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (string.IsNullOrWhiteSpace(request.Target) && string.IsNullOrWhiteSpace(request.Text))
            {
                throw NetwrightException.InvalidArgument("Wait needs a target (Ref or Selector) or text.");
            }

            var stopwatch = Stopwatch.StartNew();
            var start = Capture();
            var tree = start;
            string status;
            while (true)
            {
                (var met, status) = EvaluateWait(request, tree);
                if (met)
                {
                    break;
                }

                if (stopwatch.ElapsedMilliseconds >= request.TimeoutMs)
                {
                    throw new NetwrightException(
                        ErrorCodes.Timeout,
                        $"Waited {request.TimeoutMs} ms but {status}.",
                        "Increase timeout_ms, or take a desktop_snapshot to see the current state.");
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                tree = CaptureLight();
            }

            if (!ReferenceEquals(tree, start))
            {
                tree = Capture();
                (_, status) = EvaluateWait(request, tree);
            }

            var changes = ChangeReporter.Build(start, tree, Renderer(), _options.ChangeReportMaxLines);
            var summary = string.Create(CultureInfo.InvariantCulture, $"{status} after {stopwatch.ElapsedMilliseconds} ms");
            if (request.Target is { } target && TryResolveForRecording(target, tree) is { } selector)
            {
                Recorder.Record("wait", selector, ("state", request.State.ToString()), ("timeout", request.TimeoutMs.ToString(CultureInfo.InvariantCulture)));
            }
            else if (request.Text is not null)
            {
                Recorder.Record("wait", null, ("text", request.Text), ("state", request.State.ToString()), ("timeout", request.TimeoutMs.ToString(CultureInfo.InvariantCulture)));
            }

            return new ActionOutcome(summary, changes, true, stopwatch.ElapsedMilliseconds);
        }, cancellationToken);

    public Task<string> ExpectAsync(string target, ExpectRequest request, CancellationToken cancellationToken = default) =>
        ExclusiveAsync(async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.Assertion is Assertion.Text or Assertion.TextContains or Assertion.Value or Assertion.Count && request.Expected is null)
            {
                throw NetwrightException.InvalidArgument($"Assertion '{request.Assertion}' needs an expected value.");
            }

            var stopwatch = Stopwatch.StartNew();
            var first = true;
            while (true)
            {
                var tree = first ? Capture() : CaptureLight();
                var (pass, description, node) = EvaluateExpectation(target, request, tree);
                if (pass && !first)
                {
                    // Re-evaluate on a full capture so the reported line carries every attribute.
                    tree = Capture();
                    (pass, description, node) = EvaluateExpectation(target, request, tree);
                }

                first = false;
                if (pass)
                {
                    var selector = node is not null ? SelectorBuilder.Build(node, tree).ToString() : RefRegistry.LooksLikeRef(target) ? null : target;
                    Recorder.Record("expect", selector, ("assertion", request.Assertion.ToString()), ("expected", request.Expected));
                    return $"pass: {description}";
                }

                if (stopwatch.ElapsedMilliseconds >= request.TimeoutMs)
                {
                    throw new NetwrightException(
                        ErrorCodes.ExpectationFailed,
                        string.Create(CultureInfo.InvariantCulture, $"fail: {description} (waited {request.TimeoutMs} ms)"));
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
        }, cancellationToken);

    public LogsOutcome ReadLogs(string? stream = null, int max = 100, bool includeAlreadyRead = false)
    {
        var (entries, skipped) = _output.Read(stream, Math.Clamp(max, 1, 2000), includeAlreadyRead);
        var warning = _app is { LaunchedBySession: false }
            ? "stdout/stderr are only captured for apps launched by Netwright; debug output is captured for attached apps too."
            : _debugWarning;
        return new LogsOutcome(entries, skipped, warning);
    }

    public Task<string> ListWindowsAsync(CancellationToken cancellationToken = default) =>
        ExclusiveAsync(() =>
        {
            var tree = Capture();
            var renderer = Renderer();
            var lines = tree.Windows.Select(w => "- " + renderer.RenderLine(w)).ToList();
            return Task.FromResult(lines.Count == 0 ? "The Target App has no open windows." : string.Join(Environment.NewLine, lines));
        }, cancellationToken);

    private (bool Met, string Status) EvaluateWait(WaitRequest request, UiTree tree)
    {
        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            var match = tree.AllNodes.FirstOrDefault(n => !IsHidden(n) && (Contains(n.Name, request.Text) || Contains(n.Value, request.Text)));
            var wantGone = request.State is ElementState.Gone or ElementState.Hidden;
            return wantGone
                ? (match is null, match is null ? $"text \"{request.Text}\" is gone" : $"text \"{request.Text}\" is still shown")
                : (match is not null, match is not null ? $"\"{request.Text}\" appeared in {Renderer().RenderLine(match, withAttributes: false)}" : $"text \"{request.Text}\" has not appeared");
        }

        UiNode? node;
        try
        {
            node = Resolve(request.Target!, tree).Node;
        }
        catch (NetwrightException ex) when (ex.Code is ErrorCodes.ElementNotFound or ErrorCodes.StaleRef)
        {
            node = null;
        }

        var line = node is null ? $"'{request.Target}'" : Renderer().RenderLine(node, withAttributes: false);
        return request.State switch
        {
            ElementState.Exists => (node is not null, node is not null ? $"{line} exists" : $"{line} does not exist"),
            ElementState.Gone => (node is null, node is null ? $"{line} is gone" : $"{line} still exists"),
            ElementState.Visible => (node is not null && !IsHidden(node), node is null ? $"{line} does not exist" : IsHidden(node) ? $"{line} is not visible" : $"{line} is visible"),
            ElementState.Hidden => (node is null || IsHidden(node), node is null || IsHidden(node) ? $"{line} is hidden" : $"{line} is still visible"),
            ElementState.Enabled => (node is { IsEnabled: true }, node is null ? $"{line} does not exist" : node.IsEnabled ? $"{line} is enabled" : $"{line} is still disabled"),
            ElementState.Disabled => (node is { IsEnabled: false }, node is null ? $"{line} does not exist" : !node.IsEnabled ? $"{line} is disabled" : $"{line} is still enabled"),
            _ => (false, "unknown state"),
        };
    }

    private (bool Pass, string Description, UiNode? Node) EvaluateExpectation(string target, ExpectRequest request, UiTree tree)
    {
        if (request.Assertion == Assertion.Count)
        {
            if (RefRegistry.LooksLikeRef(target))
            {
                throw NetwrightException.InvalidArgument("The count assertion needs a Selector, not a Ref.");
            }

            var expectedCount = int.Parse(request.Expected!, CultureInfo.InvariantCulture);
            var countTree = tree.IncludesOffscreen ? tree : tree.IsLight ? CaptureLight(includeOffscreen: true) : Capture(includeOffscreen: true);
            var count = SelectorMatcher.Match(SelectorParser.Parse(target), countTree).Count;
            return (count == expectedCount, $"'{target}' matches {count} element(s), expected {expectedCount}", null);
        }

        UiNode? node;
        try
        {
            node = Resolve(target, tree).Node;
        }
        catch (NetwrightException ex) when (ex.Code is ErrorCodes.ElementNotFound or ErrorCodes.StaleRef)
        {
            node = null;
        }

        if (node is null)
        {
            var gone = request.Assertion is Assertion.Gone or Assertion.Hidden;
            return (gone, gone ? $"'{target}' does not exist" : $"expected '{target}' to be {Describe(request)} but it does not exist", null);
        }

        var line = Renderer().RenderLine(node, withAttributes: false);
        var isChecked = node.Toggle == ToggleValue.On || (node.ControlTypeId == ControlTypeIds.RadioButton && node.IsSelected == true);
        var text = node.Text.Trim();
        var (pass, actual) = request.Assertion switch
        {
            Assertion.Exists => (true, "it exists"),
            Assertion.Gone => (false, "it still exists"),
            Assertion.Visible => (!IsHidden(node), IsHidden(node) ? "it is not visible" : "it is visible"),
            Assertion.Hidden => (IsHidden(node), IsHidden(node) ? "it is hidden" : "it is visible"),
            Assertion.Enabled => (node.IsEnabled, node.IsEnabled ? "it is enabled" : "it is disabled"),
            Assertion.Disabled => (!node.IsEnabled, node.IsEnabled ? "it is enabled" : "it is disabled"),
            Assertion.Checked => (isChecked, isChecked ? "it is checked" : "it is unchecked"),
            Assertion.Unchecked => (!isChecked, isChecked ? "it is checked" : "it is unchecked"),
            Assertion.Selected => (node.IsSelected == true, node.IsSelected == true ? "it is selected" : "it is not selected"),
            Assertion.NotSelected => (node.IsSelected != true, node.IsSelected == true ? "it is selected" : "it is not selected"),
            Assertion.Expanded => (node.Expand == ExpandValue.Expanded, $"it is {node.Expand?.ToString().ToLowerInvariant() ?? "not expandable"}"),
            Assertion.Collapsed => (node.Expand == ExpandValue.Collapsed, $"it is {node.Expand?.ToString().ToLowerInvariant() ?? "not expandable"}"),
            Assertion.Focused => (node.HasFocus, node.HasFocus ? "it has focus" : "it does not have focus"),
            Assertion.Text => (string.Equals(text, request.Expected!.Trim(), StringComparison.OrdinalIgnoreCase), $"its text is \"{text}\""),
            Assertion.TextContains => (text.Contains(request.Expected!.Trim(), StringComparison.OrdinalIgnoreCase), $"its text is \"{text}\""),
            Assertion.Value => (string.Equals(node.Value?.Trim(), request.Expected!.Trim(), StringComparison.Ordinal), $"its value is \"{node.Value}\""),
            _ => (false, "unsupported assertion"),
        };

        return (pass, pass ? $"{line} {Describe(request)}" : $"expected {line} to be {Describe(request)} but {actual}", node);
    }

    private static string Describe(ExpectRequest request) => request.Assertion switch
    {
        Assertion.Text => $"text \"{request.Expected}\"",
        Assertion.TextContains => $"containing \"{request.Expected}\"",
        Assertion.Value => $"value \"{request.Expected}\"",
        Assertion.NotSelected => "not selected",
        _ => request.Assertion.ToString().ToLowerInvariant(),
    };

    private string? TryResolveForRecording(string target, UiTree tree)
    {
        try
        {
            var (node, found) = Resolve(target, tree);
            return SelectorBuilder.Build(node, found).ToString();
        }
        catch (NetwrightException)
        {
            return RefRegistry.LooksLikeRef(target) ? null : target;
        }
    }

    private static bool IsHidden(UiNode node) => node.IsOffscreen || node.Ancestors().Any(a => a.IsOffscreen && a.Parent is not null);

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";

    internal AppOutput Output => _output;
}
