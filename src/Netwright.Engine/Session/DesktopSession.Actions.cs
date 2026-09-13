using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using Interop.UIAutomationClient;
using Netwright.Engine.Input;
using Netwright.Engine.Model;
using Netwright.Engine.Selectors;
using Netwright.Engine.Snapshots;
using Netwright.Engine.Uia;

namespace Netwright.Engine.Session;

public sealed partial class DesktopSession
{
    [Flags]
    private enum Actionability
    {
        Exists = 0,
        Enabled = 1,
        Visible = 2,
        Ready = Enabled | Visible,
    }

    private sealed class ActionContext(UiNode node, UiTree before)
    {
        public UiNode Node { get; } = node;

        public UiTree Before { get; } = before;

        private int _abandoned;

        /// <summary>Set before any call that may block, so the summary survives a modal dialog.</summary>
        public string Summary { get; set; } = "";

        /// <summary>
        /// Set when the pipeline stopped waiting for a blocked call and released the session. The
        /// blocked work must not send any further input once it resumes, because by then another
        /// action may own the foreground.
        /// </summary>
        public bool Abandoned
        {
            get => Volatile.Read(ref _abandoned) == 1;
            set => Volatile.Write(ref _abandoned, value ? 1 : 0);
        }
    }

    public Task<ActionOutcome> ClickAsync(string target, ClickRequest? request = null, CancellationToken cancellationToken = default)
    {
        request ??= new ClickRequest();
        if (request.ClickCount is < 1 or > 2)
        {
            throw NetwrightException.InvalidArgument("click_count must be 1 or 2.");
        }

        var simpleClick = request.Button == MouseButtonKind.Left && request.ClickCount == 1;
        if (!simpleClick && !request.Foreground)
        {
            throw NeedsForeground($"A {(request.ClickCount == 2 ? "double" : request.Button.ToString().ToLowerInvariant())} click needs real mouse input.");
        }

        return ActAsync(target, request.Foreground ? Actionability.Ready : Actionability.Enabled, request.Foreground, context =>
        {
            var node = context.Node;
            var line = Line(node);
            if (request.Foreground)
            {
                var point = ClickPoint(node);
                if (context.Abandoned)
                {
                    return;
                }

                context.Summary = $"{(request.ClickCount == 2 ? "double-clicked" : request.Button == MouseButtonKind.Left ? "clicked" : $"{request.Button.ToString().ToLowerInvariant()}-clicked")} {line} with the mouse";
                var button = request.Button switch
                {
                    MouseButtonKind.Right => MouseButton.Right,
                    MouseButtonKind.Middle => MouseButton.Middle,
                    _ => MouseButton.Left,
                };
                Mouse.MoveTo(point);
                if (request.ClickCount == 2)
                {
                    Mouse.DoubleClick(point, button);
                }
                else
                {
                    Mouse.Click(point, button);
                }

                Wait.UntilInputIsProcessed();
                return;
            }

            if (node.Has(UiPatterns.Invoke))
            {
                context.Summary = $"clicked {line}";
                PatternCalls.Invoke(node);
            }
            else if (node.Has(UiPatterns.Toggle))
            {
                context.Summary = $"toggled {line}";
                PatternCalls.Toggle(node);
            }
            else if (node.Has(UiPatterns.SelectionItem))
            {
                context.Summary = $"selected {line}";
                PatternCalls.Select(node);
            }
            else if (node.Has(UiPatterns.ExpandCollapse))
            {
                var expanded = node.Expand is ExpandValue.Expanded or ExpandValue.PartiallyExpanded;
                context.Summary = $"{(expanded ? "collapsed" : "expanded")} {line}";
                if (expanded)
                {
                    PatternCalls.Collapse(node);
                }
                else
                {
                    PatternCalls.Expand(node);
                }
            }
            else if (node.Has(UiPatterns.LegacyIAccessible) && PatternCalls.DefaultAction(node).Length > 0)
            {
                context.Summary = $"clicked {line} (default action)";
                PatternCalls.DoDefaultAction(node);
            }
            else
            {
                throw NeedsForeground($"{line} cannot be clicked without the mouse (no Invoke, Toggle, SelectionItem or ExpandCollapse pattern).");
            }
        }, "click", recordArgs: [("button", request.Button == MouseButtonKind.Left ? null : request.Button.ToString().ToLowerInvariant()), ("count", request.ClickCount == 1 ? null : "2"), ("foreground", request.Foreground ? "true" : null)], cancellationToken);
    }

    public Task<ActionOutcome> TypeAsync(string target, TypeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Submit && !request.Foreground)
        {
            throw NeedsForeground("Submitting with Enter needs real keyboard input.");
        }

        return ActAsync(target, request.Foreground ? Actionability.Ready : Actionability.Enabled, request.Foreground, context =>
        {
            var node = context.Node;
            var line = Line(node);
            if (request.Foreground)
            {
                context.Summary = string.Create(CultureInfo.InvariantCulture, $"typed {request.Text.Length} chars into {line} with the keyboard{(request.Submit ? " and pressed Enter" : "")}");
                PatternCalls.SetFocus(node);
                Wait.UntilInputIsProcessed();
                if (context.Abandoned)
                {
                    return;
                }

                if (request.Clear)
                {
                    Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
                    Keyboard.Type(VirtualKeyShort.DELETE);
                }

                if (request.Text.Length > 0)
                {
                    Keyboard.Type(request.Text);
                }

                if (request.Submit)
                {
                    Keyboard.Type(VirtualKeyShort.RETURN);
                }

                Wait.UntilInputIsProcessed();
                return;
            }

            if (!node.Has(UiPatterns.Value))
            {
                throw NeedsForeground($"{line} does not accept text without the keyboard (no Value pattern).");
            }

            if (node.ValueIsReadOnly)
            {
                throw new NetwrightException(ErrorCodes.NotActionable, $"{line} is read-only.");
            }

            var value = request.Clear ? request.Text : (node.Value ?? "") + request.Text;
            context.Summary = $"set text of {line}";
            PatternCalls.SetValue(node, value);
        }, "type", recordArgs: [("text", request.Text), ("clear", request.Clear ? null : "false"), ("submit", request.Submit ? "true" : null), ("foreground", request.Foreground ? "true" : null)], cancellationToken);
    }

    public Task<ActionOutcome> SetStateAsync(string target, SetStateRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requested = new object?[] { request.Checked, request.Selected, request.Expanded, request.Value, request.Item }.Count(v => v is not null);
        if (requested != 1)
        {
            throw NetwrightException.InvalidArgument("Set exactly one of checked, selected, expanded, value or item.");
        }

        return ActAsync(target, Actionability.Enabled, foreground: false, context =>
        {
            var node = context.Node;
            var line = Line(node);

            if (request.Checked is { } wantChecked)
            {
                if (node.Has(UiPatterns.Toggle))
                {
                    context.Summary = $"{(wantChecked ? "checked" : "unchecked")} {line}";
                    for (var i = 0; i < 3 && (PatternCalls.CurrentToggleState(node) == ToggleState.ToggleState_On) != wantChecked; i++)
                    {
                        PatternCalls.Toggle(node);
                    }
                }
                else if (node.Has(UiPatterns.SelectionItem) && wantChecked)
                {
                    context.Summary = $"checked {line}";
                    PatternCalls.Select(node);
                }
                else
                {
                    throw new NetwrightException(ErrorCodes.NotSupported, $"{line} cannot be checked (no Toggle pattern).");
                }
            }
            else if (request.Selected is { } wantSelected)
            {
                context.Summary = $"{(wantSelected ? "selected" : "deselected")} {line}";
                if (wantSelected)
                {
                    PatternCalls.Select(node);
                }
                else
                {
                    PatternCalls.RemoveFromSelection(node);
                }
            }
            else if (request.Expanded is { } wantExpanded)
            {
                context.Summary = $"{(wantExpanded ? "expanded" : "collapsed")} {line}";
                if (wantExpanded)
                {
                    PatternCalls.Expand(node);
                }
                else
                {
                    PatternCalls.Collapse(node);
                }
            }
            else if (request.Value is { } value)
            {
                if (node.Has(UiPatterns.RangeValue))
                {
                    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    {
                        throw NetwrightException.InvalidArgument($"{line} takes a number, got \"{value}\".");
                    }

                    context.Summary = $"set {line} to {value}";
                    PatternCalls.SetRangeValue(node, number);
                }
                else
                {
                    context.Summary = $"set {line} to \"{Truncate(value, 60)}\"";
                    PatternCalls.SetValue(node, value);
                }
            }
            else
            {
                SelectItem(context, request.Item!);
            }
        }, "set_state", recordArgs: [("checked", Lower(request.Checked)), ("selected", Lower(request.Selected)), ("expanded", Lower(request.Expanded)), ("value", request.Value), ("item", request.Item)], cancellationToken);
    }

    public Task<ActionOutcome> PressKeyAsync(string? target, string keys, bool foreground, CancellationToken cancellationToken = default)
    {
        var chords = KeyParser.Parse(keys);
        if (!foreground)
        {
            throw NeedsForeground("Key presses need real keyboard input, which Windows only delivers to the focused window.");
        }

        return ActCoreAsync(
            () => LocateAsync(target, Actionability.Enabled, cancellationToken),
            foreground: true,
            context =>
            {
                context.Summary = $"pressed {keys}" + (target is null ? "" : $" in {Line(context.Node)}");
                if (target is not null)
                {
                    PatternCalls.SetFocus(context.Node);
                    Wait.UntilInputIsProcessed();
                }

                if (context.Abandoned)
                {
                    return;
                }

                foreach (var chord in chords)
                {
                    if (chord.Length == 1)
                    {
                        Keyboard.Type(chord[0]);
                    }
                    else
                    {
                        Keyboard.TypeSimultaneously(chord);
                    }
                }

                Wait.UntilInputIsProcessed();
            },
            "press_key",
            [("keys", keys)],
            recordSelector: target is not null,
            cancellationToken);
    }

    public Task<ActionOutcome> ScrollAsync(string target, ScrollRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.IntoView && request.Direction is null && request.To is null)
        {
            throw NetwrightException.InvalidArgument("Scroll needs direction, to, or into_view=true.");
        }

        return ActAsync(target, Actionability.Exists, foreground: false, context =>
        {
            var node = context.Node;
            if (request.IntoView)
            {
                context.Summary = $"scrolled {Line(node)} into view";
                if (node.Has(UiPatterns.ScrollItem))
                {
                    PatternCalls.ScrollIntoView(node);
                    return;
                }

                throw new NetwrightException(ErrorCodes.NotSupported, $"{Line(node)} cannot be scrolled into view (no ScrollItem pattern).", "Scroll its container with direction instead.");
            }

            var container = node.Has(UiPatterns.Scroll) ? node : node.Ancestors().FirstOrDefault(a => a.Has(UiPatterns.Scroll))
                ?? throw new NetwrightException(ErrorCodes.NotSupported, $"Neither {Line(node)} nor its ancestors can scroll.");

            const double noScroll = -1;
            double percent;
            if (request.To is { } edge)
            {
                percent = edge switch
                {
                    ScrollEdge.Top => PatternCalls.SetScrollPercent(container, noScroll, 0),
                    ScrollEdge.Bottom => PatternCalls.SetScrollPercent(container, noScroll, 100),
                    ScrollEdge.Start => PatternCalls.SetScrollPercent(container, 0, noScroll),
                    _ => PatternCalls.SetScrollPercent(container, 100, noScroll),
                };
                context.Summary = $"scrolled {Line(container)} to the {edge.ToString().ToLowerInvariant()}";
            }
            else
            {
                var increment = request.Amount == ScrollAmountKind.Page ? ScrollAmount.ScrollAmount_LargeIncrement : ScrollAmount.ScrollAmount_SmallIncrement;
                var decrement = request.Amount == ScrollAmountKind.Page ? ScrollAmount.ScrollAmount_LargeDecrement : ScrollAmount.ScrollAmount_SmallDecrement;
                var none = ScrollAmount.ScrollAmount_NoAmount;
                percent = request.Direction switch
                {
                    ScrollDirection.Up => PatternCalls.Scroll(container, none, decrement),
                    ScrollDirection.Down => PatternCalls.Scroll(container, none, increment),
                    ScrollDirection.Left => PatternCalls.Scroll(container, decrement, none),
                    _ => PatternCalls.Scroll(container, increment, none),
                };
                context.Summary = $"scrolled {Line(container)} {request.Direction!.Value.ToString().ToLowerInvariant()}";
            }

            if (percent >= 0)
            {
                context.Summary += string.Create(CultureInfo.InvariantCulture, $" (vertical {percent:0}%)");
            }
        }, "scroll", recordArgs: [("direction", request.Direction?.ToString().ToLowerInvariant()), ("to", request.To?.ToString().ToLowerInvariant()), ("into_view", request.IntoView ? "true" : null)], cancellationToken);
    }

    public Task<ActionOutcome> WindowAsync(string? target, WindowAction action, bool foreground, CancellationToken cancellationToken = default)
    {
        if (action == WindowAction.List)
        {
            throw NetwrightException.InvalidArgument("Use ListWindowsAsync for action=list.");
        }

        if (action == WindowAction.Activate && !foreground)
        {
            throw NeedsForeground("Activating a window takes focus from the User.");
        }

        return ActCoreAsync(
            async () =>
            {
                var (node, tree) = await LocateAsync(target, Actionability.Exists, cancellationToken).ConfigureAwait(false);
                return (target is null ? MainWindow(tree, RequireApp()) : node.Window, tree);
            },
            foreground: action == WindowAction.Activate,
            context =>
            {
                var window = context.Node;
                context.Summary = $"{action.ToString().ToLowerInvariant()}d {Line(window)}";
                switch (action)
                {
                    case WindowAction.Minimize:
                        PatternCalls.SetWindowState(window, WindowVisualState.WindowVisualState_Minimized);
                        break;
                    case WindowAction.Maximize:
                        PatternCalls.SetWindowState(window, WindowVisualState.WindowVisualState_Maximized);
                        break;
                    case WindowAction.Restore:
                        PatternCalls.SetWindowState(window, WindowVisualState.WindowVisualState_Normal);
                        break;
                    case WindowAction.Close:
                        PatternCalls.CloseWindow(window);
                        break;
                    case WindowAction.Activate:
                        break;
                }
            },
            "window",
            [("action", action.ToString().ToLowerInvariant())],
            recordSelector: target is not null,
            cancellationToken,
            keepFocus: action == WindowAction.Activate);
    }

    // ---------------------------------------------------------------- pipeline

    private Task<ActionOutcome> ActAsync(
        string target,
        Actionability need,
        bool foreground,
        Action<ActionContext> perform,
        string kind,
        (string Name, string? Value)[] recordArgs,
        CancellationToken cancellationToken) =>
        ActCoreAsync(() => LocateAsync(target, need, cancellationToken), foreground, perform, kind, recordArgs, recordSelector: true, cancellationToken);

    private Task<ActionOutcome> ActCoreAsync(
        Func<Task<(UiNode Node, UiTree Tree)>> locate,
        bool foreground,
        Action<ActionContext> perform,
        string kind,
        (string Name, string? Value)[] recordArgs,
        bool recordSelector,
        CancellationToken cancellationToken,
        bool keepFocus = false) =>
        ExclusiveAsync(async () =>
        {
            var stopwatch = Stopwatch.StartNew();
            var (node, before) = await locate().ConfigureAwait(false);
            var context = new ActionContext(node, before);
            string? note = null;
            var recordedSelector = recordSelector ? SelectorBuilder.Build(node, before).ToString() : null;

            var windowHandle = UiaTreeReader.LiveWindowHandle(node.Window);
            if (windowHandle == 0 && before.Windows.FirstOrDefault(w => w.DescendantsAndSelf().Any(d => d.Key == node.Key)) is { } root)
            {
                windowHandle = root.WindowHandle;
            }

            ForegroundScope? scope = foreground ? ForegroundScope.Enter(windowHandle) : null;
            try
            {
                var work = Task.Run(() => perform(context), cancellationToken);
                var finished = await Task.WhenAny(work, Task.Delay(_options.BlockingCallTimeoutMs, cancellationToken)).ConfigureAwait(false);
                if (finished == work)
                {
                    await work.ConfigureAwait(false);
                }
                else
                {
                    context.Abandoned = true;
                    _ = work.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    note = "The call is still running inside the Target App, which usually means it opened a modal dialog.";
                    if (context.Summary.Length == 0)
                    {
                        context.Summary = $"{kind} on {Line(node)}";
                    }
                }

                var (after, settled) = await SettleAsync(cancellationToken).ConfigureAwait(false);
                if (scope is not null && (keepFocus || after.Windows.Any(w => before.Find(w.Key) is null)))
                {
                    scope.KeepFocus();
                    if (!keepFocus)
                    {
                        note = Append(note, "Focus stays on the Target App because the action opened a window or menu.");
                    }
                }

                if (_app is null || _app.HasExited)
                {
                    note = Append(note, "The Target App exited.");
                }

                Recorder.Record(kind, recordedSelector, recordArgs);
                var changes = ChangeReporter.Build(before, after, Renderer(), _options.ChangeReportMaxLines, node.Key);
                return new ActionOutcome(context.Summary, changes, settled, stopwatch.ElapsedMilliseconds, note);
            }
            finally
            {
                scope?.Dispose();
            }
        }, cancellationToken);

    /// <summary>
    /// Waits until the target exists and is Actionable, scrolling it into view once if it is offscreen.
    /// Returns the element together with the tree it was found in, which also serves as the
    /// "before" state for the Change Report.
    /// </summary>
    private async Task<(UiNode Node, UiTree Tree)> LocateAsync(string? target, Actionability need, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var scrolled = false;
        string? problem = null;

        while (true)
        {
            var tree = Capture();
            UiNode? node = null;
            if (target is null)
            {
                node = tree.Focused ?? MainWindow(tree, RequireApp());
            }
            else
            {
                try
                {
                    (node, tree) = Resolve(target, tree);
                }
                catch (NetwrightException ex) when (ex.Code == ErrorCodes.ElementNotFound && !Netwright.Engine.Refs.RefRegistry.LooksLikeRef(target.Trim()))
                {
                    problem = ex.Message;
                    if (stopwatch.ElapsedMilliseconds >= _options.ActionTimeoutMs)
                    {
                        throw new NetwrightException(ex.Code, string.Create(CultureInfo.InvariantCulture, $"{ex.Message} (waited {_options.ActionTimeoutMs} ms)"), ex.Hint);
                    }
                }
            }

            if (node is not null)
            {
                problem = null;
                if (need.HasFlag(Actionability.Enabled) && !node.IsEnabled)
                {
                    problem = $"{Line(node)} is disabled";
                }
                else if (need.HasFlag(Actionability.Visible) && IsHidden(node))
                {
                    if (!scrolled && node.Has(UiPatterns.ScrollItem))
                    {
                        scrolled = true;
                        try
                        {
                            PatternCalls.ScrollIntoView(node);
                        }
                        catch (NetwrightException)
                        {
                        }

                        continue;
                    }

                    problem = $"{Line(node)} is offscreen";
                }

                if (problem is null)
                {
                    return (node, tree);
                }

                if (stopwatch.ElapsedMilliseconds >= _options.ActionTimeoutMs)
                {
                    throw new NetwrightException(
                        ErrorCodes.NotActionable,
                        string.Create(CultureInfo.InvariantCulture, $"{problem} (waited {_options.ActionTimeoutMs} ms)."),
                        problem.EndsWith("disabled", StringComparison.Ordinal)
                            ? "Something else must happen first (e.g. fill required fields); check the screen with desktop_snapshot."
                            : "Scroll it into view with desktop_scroll into_view=true.");
                }
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Polls cheap captures until two consecutive ones look the same (and the quiet period has
    /// passed), then takes one full capture for the Change Report.
    /// </summary>
    private async Task<(UiTree Tree, bool Settled)> SettleAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        long? previousFingerprint = null;
        var settled = false;
        await Task.Delay(30, cancellationToken).ConfigureAwait(false);

        while (true)
        {
            if (_app is not { HasExited: false } app)
            {
                return (UiTree.Empty, true);
            }

            long fingerprint;
            try
            {
                fingerprint = _reader.Capture(app.ProcessIds, CaptureDetail.Light).Fingerprint();
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                if (stopwatch.ElapsedMilliseconds >= _options.SettleTimeoutMs)
                {
                    break;
                }

                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (fingerprint == previousFingerprint && stopwatch.ElapsedMilliseconds >= _options.SettleQuietMs)
            {
                settled = true;
                break;
            }

            if (stopwatch.ElapsedMilliseconds >= _options.SettleTimeoutMs)
            {
                break;
            }

            previousFingerprint = fingerprint;
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        if (_app is not { HasExited: false } current)
        {
            return (UiTree.Empty, true);
        }

        try
        {
            return (_reader.Capture(current.ProcessIds), settled);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return (UiTree.Empty, false);
        }
    }

    private void SelectItem(ActionContext context, string itemName)
    {
        var owner = context.Node;
        var line = Line(owner);
        var items = Items(owner);
        var expandedByUs = false;

        var match = FindItem(items, itemName);
        if (match is null && owner.Has(UiPatterns.ExpandCollapse) && owner.Expand == ExpandValue.Collapsed)
        {
            PatternCalls.Expand(owner);
            expandedByUs = true;

            // Items are generated asynchronously when the drop-down opens, and some frameworks expose
            // them in the popup window rather than under the combo box itself.
            for (var attempt = 0; attempt < 15 && match is null; attempt++)
            {
                Thread.Sleep(attempt == 0 ? 50 : 100);
                items = Items(_reader.CaptureElement(owner.Native!));
                if (items.Count == 0 && _app is { } app)
                {
                    var popups = _reader.Capture(app.ProcessIds, CaptureDetail.Full, includeOffscreen: true).Windows
                        .Where(w => context.Before.Find(w.Key) is null);
                    items = popups.SelectMany(Items).ToList();
                }

                match = FindItem(items, itemName);
                if (items.Count > 0 && match is null)
                {
                    break;
                }
            }
        }

        if (match is null)
        {
            if (expandedByUs)
            {
                TryCollapse(owner);
            }

            var available = items.Select(i => i.Name).Where(n => n.Length > 0).Distinct().Take(15).ToList();
            throw new NetwrightException(
                ErrorCodes.ElementNotFound,
                $"{line} has no item named \"{itemName}\".",
                available.Count > 0 ? $"Available: {string.Join(", ", available)}" : "The list is empty or its items are not loaded yet.");
        }

        context.Summary = $"selected \"{match.Name}\" in {line}";
        if (match.Has(UiPatterns.SelectionItem))
        {
            PatternCalls.Select(match);
        }
        else if (match.Has(UiPatterns.Invoke))
        {
            PatternCalls.Invoke(match);
        }
        else
        {
            throw new NetwrightException(ErrorCodes.NotSupported, $"Item \"{match.Name}\" cannot be selected (no SelectionItem or Invoke pattern).");
        }

        if (expandedByUs)
        {
            TryCollapse(owner);
        }

        static List<UiNode> Items(UiNode root) => root.Descendants()
            .Where(n => n.ControlTypeId is ControlTypeIds.ListItem or ControlTypeIds.TreeItem or ControlTypeIds.TabItem or ControlTypeIds.DataItem or ControlTypeIds.MenuItem or ControlTypeIds.RadioButton)
            .ToList();
    }

    private static UiNode? FindItem(List<UiNode> items, string name)
    {
        var exact = items.Where(i => string.Equals(i.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 0)
        {
            return exact[0];
        }

        var partial = items.Where(i => i.Name.Contains(name.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return partial.Count == 1 ? partial[0] : null;
    }

    private static void TryCollapse(UiNode node)
    {
        try
        {
            if (PatternCalls.CurrentExpandState(node) != ExpandCollapseState.ExpandCollapseState_Collapsed)
            {
                PatternCalls.Collapse(node);
            }
        }
        catch (NetwrightException)
        {
            // Many combo boxes close on selection by themselves.
        }
    }

    private static Point ClickPoint(UiNode node)
    {
        var bounds = UiaTreeReader.LiveBounds(node);
        if (bounds.IsEmpty)
        {
            throw new NetwrightException(ErrorCodes.NotActionable, $"{PatternCalls.Describe(node)} has no on-screen area to click.");
        }

        return new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    private string Line(UiNode node) => Renderer().RenderLine(node, withAttributes: false);

    private static NetwrightException NeedsForeground(string reason) => new(
        ErrorCodes.NeedsForeground,
        reason,
        "Retry with foreground: true. Netwright briefly takes focus and the mouse, then gives them back to the User.");

    private static string? Lower(bool? value) => value?.ToString().ToLowerInvariant();

    private static string? Append(string? note, string addition) => note is null ? addition : note + " " + addition;
}
