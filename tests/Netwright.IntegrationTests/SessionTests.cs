using System.Diagnostics;
using System.Text.RegularExpressions;
using Netwright.Engine;
using Netwright.Engine.Session;
using Netwright.Engine.Uia;

namespace Netwright.IntegrationTests;

/// <summary>
/// End-to-end behaviour of the Engine against the real Fixture Apps. Every test launches its own
/// Target App and disposes the session, which kills it.
/// </summary>
[Collection("UI")]
public sealed partial class SessionTests(ITestOutputHelper output)
{
    public static TheoryData<FixtureKind> Kinds => new() { FixtureKind.Wpf, FixtureKind.WinForms };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Snapshot_shows_the_form_with_refs_ids_and_states(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);

        var snapshot = (await session.SnapshotAsync()).Text;
        output.WriteLine(snapshot);

        Assert.Matches(@"textbox ""Name"" \[e\d+\] #txtName", snapshot);
        Assert.Matches(@"button ""Submit"" \[e\d+\] #btnSubmit disabled", snapshot);
        Assert.Matches(@"checkbox ""Accept terms"" \[e\d+\] #chkTerms unchecked", snapshot);
        Assert.DoesNotContain("mixed", snapshot, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Refs_stay_the_same_across_snapshots(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);

        var first = RefOf((await session.SnapshotAsync()).Text, "#btnSubmit");
        await session.TypeAsync("#txtName", new TypeRequest { Text = "changed" });
        var second = RefOf((await session.SnapshotAsync()).Text, "#btnSubmit");

        Assert.Equal(first, second);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Form_submit_flow_reports_the_result_without_a_new_snapshot(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);

        var typed = await session.TypeAsync("#txtName", new TypeRequest { Text = "Ada Lovelace" });
        Assert.Contains("value=\"Ada Lovelace\"", typed.Changes.Text, StringComparison.Ordinal);
        await session.TypeAsync("#txtEmail", new TypeRequest { Text = "ada@example.com" });
        await session.SetStateAsync("#cmbCountry", new SetStateRequest { Item = "Italy" });

        var checkedTerms = await session.SetStateAsync("#chkTerms", new SetStateRequest { Checked = true });
        Assert.Contains("checked", checkedTerms.Changes.Text, StringComparison.Ordinal);
        Assert.Matches(@"~ button ""Submit"" \[e\d+\] #btnSubmit(?! disabled)", checkedTerms.Changes.Text);

        var clicked = await session.ClickAsync("#btnSubmit");
        output.WriteLine(clicked.Changes.Text);

        Assert.True(clicked.Settled);
        Assert.Contains("Submitted: Ada Lovelace <ada@example.com> Italy Free x1", clicked.Changes.Text, StringComparison.Ordinal);
        Assert.StartsWith("pass:", await session.ExpectAsync("#lblResult", new ExpectRequest { Assertion = Assertion.TextContains, Expected = "Italy" }), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Actions_wait_for_elements_to_become_enabled(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, "Async");

        await session.ClickAsync("#btnEnableLater");
        var stopwatch = Stopwatch.StartNew();
        await session.ClickAsync("#btnLater");

        Assert.True(stopwatch.ElapsedMilliseconds < 4000);
        await session.ExpectAsync("#lblLater", new ExpectRequest { Assertion = Assertion.Text, Expected = "Later clicked" });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Disabled_elements_fail_with_a_clear_error_after_the_timeout(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, options: new SessionOptions { ActionTimeoutMs = 400 });

        var ex = await Assert.ThrowsAsync<NetwrightException>(() => session.ClickAsync("#btnSubmit"));

        Assert.Equal(ErrorCodes.NotActionable, ex.Code);
        Assert.Contains("disabled", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Waiting_for_text_reports_what_the_async_load_changed(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, "Async");

        var click = await session.ClickAsync("#btnLoad");
        Assert.Contains("Loading...", click.Changes.Text, StringComparison.Ordinal);

        var waited = await session.WaitAsync(new WaitRequest { Text = "Loaded 3 items", TimeoutMs = 5000 });
        output.WriteLine(waited.Summary + Environment.NewLine + waited.Changes.Text);

        Assert.Contains("Gamma", waited.Changes.Text, StringComparison.Ordinal);
        await session.ExpectAsync("#lstItems >> item", new ExpectRequest { Assertion = Assertion.Count, Expected = "3" });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Selector_problems_are_reported_precisely(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, options: new SessionOptions { ActionTimeoutMs = 300 });

        var ambiguous = await Assert.ThrowsAsync<NetwrightException>(() => session.ClickAsync("button"));
        Assert.Equal(ErrorCodes.AmbiguousSelector, ambiguous.Code);
        Assert.Contains("[e", ambiguous.Message, StringComparison.Ordinal);

        var missing = await Assert.ThrowsAsync<NetwrightException>(() => session.ClickAsync("#doesNotExist"));
        Assert.Equal(ErrorCodes.ElementNotFound, missing.Code);

        var invalid = await Assert.ThrowsAsync<NetwrightException>(() => session.ClickAsync("button \"unterminated"));
        Assert.Equal(ErrorCodes.InvalidSelector, invalid.Code);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Refs_to_closed_windows_are_stale(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, options: new SessionOptions { ActionTimeoutMs = 1000 });

        var opened = await session.ClickAsync("#btnOpenWindow");
        Assert.Contains("Tool Window", opened.Changes.Text, StringComparison.Ordinal);
        var noteRef = RefOf(await session.FindAsync("#txtToolNote"), "#txtToolNote");

        await session.ClickAsync("#btnToolClose");
        var ex = await Assert.ThrowsAsync<NetwrightException>(() => session.TypeAsync(noteRef, new TypeRequest { Text = "x" }));

        Assert.Equal(ErrorCodes.StaleRef, ex.Code);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Modal_dialogs_appear_in_the_change_report_and_can_be_answered(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);

        var opened = await session.ClickAsync("#btnOpenModal");
        output.WriteLine(opened.Changes.Text);
        Assert.Contains("window \"Confirm\"", opened.Changes.Text, StringComparison.Ordinal);

        await session.ClickAsync("window \"Confirm\" >> button \"Yes\"");
        await session.ExpectAsync("#lblModalResult", new ExpectRequest { Assertion = Assertion.Text, Expected = "Modal result: Yes" });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Tree_items_can_be_expanded_and_selected(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, "Tree");

        await session.SetStateAsync("treeitem \"Root\"", new SetStateRequest { Expanded = true });
        await session.SetStateAsync("treeitem \"Documents\"", new SetStateRequest { Expanded = true });
        await session.SetStateAsync("treeitem \"Reports\"", new SetStateRequest { Selected = true });

        await session.ExpectAsync("#lblSelectedNode", new ExpectRequest { Assertion = Assertion.Text, Expected = "Selected: Reports" });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Offscreen_elements_are_found_and_scrolled_into_view(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, "Scroll");

        await session.ClickAsync("#btnItem100");

        await session.ExpectAsync("#lblScrollClicked", new ExpectRequest { Assertion = Assertion.Text, Expected = "Clicked Item 100" });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Scrolling_a_container_reports_the_position(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, "Scroll");

        var scrolled = await session.ScrollAsync("#scrLong", new ScrollRequest { To = ScrollEdge.Bottom });

        Assert.Contains("vertical 100%", scrolled.Summary, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Grids_are_summarized_by_row(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, "Grid");

        var snapshot = (await session.SnapshotAsync()).Text;
        output.WriteLine(snapshot);

        Assert.Contains("rows=1000", snapshot, StringComparison.Ordinal);
        Assert.Matches(@"row ""1 \| Customer 1 \| 10\.00"" \[e\d+\]", snapshot);
        Assert.Contains("more items", snapshot, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Screenshots_render_the_window(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);

        var window = await session.ScreenshotAsync(maxSize: 800);
        var element = await session.ScreenshotAsync("#btnSubmit");

        Assert.Equal(800, Math.Max(window.Image.Width, window.Image.Height));
        Assert.Equal(0x89, window.Image.Png[0]);
        Assert.True(element.Image.Width < 400 && element.Image.Height < 200, $"element capture was {element.Image.Width}x{element.Image.Height}");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task App_output_includes_stdout_stderr_and_debug_lines(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);

        await session.ClickAsync("#btnTrace");
        await Task.Delay(300);
        var logs = session.ReadLogs();
        foreach (var entry in logs.Entries)
        {
            output.WriteLine(entry.ToString());
        }

        Assert.Contains(logs.Entries, e => e.Stream == "stdout" && e.Text.Contains("fixture-stdout: hello", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, e => e.Stream == "stderr" && e.Text.Contains("fixture-stderr: hello", StringComparison.Ordinal));
        if (logs.Warning is null)
        {
            Assert.Contains(logs.Entries, e => e.Stream == "debug" && e.Text.Contains("fixture-trace: hello", StringComparison.Ordinal));
        }

        Assert.Empty(session.ReadLogs().Entries);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Crashes_are_reported_with_the_exception(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);

        await session.ClickAsync("#btnCrash");
        var notice = await session.TakePendingNoticeAsync();
        output.WriteLine(notice ?? "(no notice)");

        Assert.NotNull(notice);
        Assert.Contains("InvalidOperationException", notice, StringComparison.Ordinal);
        Assert.Contains("Fixture crash requested", notice, StringComparison.Ordinal);
        Assert.Equal(ErrorCodes.AppExited, (await Assert.ThrowsAsync<NetwrightException>(() => session.SnapshotAsync())).Code);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Keyboard_and_mouse_need_an_explicit_foreground_action(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, "Keyboard");

        Assert.Equal(ErrorCodes.NeedsForeground, (await Assert.ThrowsAsync<NetwrightException>(() => session.PressKeyAsync("#txtKeys", "Enter", foreground: false))).Code);
        Assert.Equal(ErrorCodes.NeedsForeground, (await Assert.ThrowsAsync<NetwrightException>(() => session.ClickAsync("#pnlMouse"))).Code);

        await session.PressKeyAsync("#txtKeys", "Enter", foreground: true);
        await session.ExpectAsync("#lblLastKey", new ExpectRequest { Assertion = Assertion.TextContains, Expected = "Last key: " });
        Assert.DoesNotContain("none", (await session.FindAsync("#lblLastKey")), StringComparison.Ordinal);

        await session.ClickAsync("#pnlMouse", new ClickRequest { Button = MouseButtonKind.Right, Foreground = true });
        await session.ExpectAsync("#lblMouse", new ExpectRequest { Assertion = Assertion.Text, Expected = "Mouse: right" });

        await session.ClickAsync("#pnlMouse", new ClickRequest { ClickCount = 2, Foreground = true });
        await session.ExpectAsync("#lblMouse", new ExpectRequest { Assertion = Assertion.Text, Expected = "Mouse: double" });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Windows_can_be_minimized_and_restored(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);

        await session.WindowAsync(null, WindowAction.Minimize, foreground: false);
        Assert.Contains("minimized", await session.ListWindowsAsync(), StringComparison.Ordinal);

        await session.WindowAsync(null, WindowAction.Restore, foreground: false);
        Assert.DoesNotContain("minimized", await session.ListWindowsAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Apps_close_gracefully(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);

        Assert.StartsWith("Closed", await session.CloseAsync(force: false), StringComparison.Ordinal);
        Assert.Null(await session.TakePendingNoticeAsync());
        Assert.False(session.HasApp);
    }

    [Fact]
    public async Task Allow_list_blocks_other_apps()
    {
        await using var session = new DesktopSession(new SessionOptions { AllowedApps = ["notepad*"] });

        var ex = await Assert.ThrowsAsync<NetwrightException>(() => session.LaunchAsync(new LaunchRequest { Path = Fixtures.ExecutablePath(FixtureKind.Wpf) }));

        Assert.Equal(ErrorCodes.NotAllowed, ex.Code);
    }

    [Fact]
    public async Task Attaches_to_a_running_app_by_name()
    {
        Fixtures.KillStrays();
        using var process = Process.Start(new ProcessStartInfo(Fixtures.ExecutablePath(FixtureKind.Wpf), "--no-activate") { UseShellExecute = false })!;
        try
        {
            await using var session = new DesktopSession();
            var status = await session.AttachAsync(new AttachRequest { ProcessName = "Netwright.Fixtures.Wpf" });

            Assert.Equal(process.Id, status.ProcessId);
            Assert.Equal("WPF", status.Framework);
            Assert.False(status.LaunchedBySession);
            Assert.Contains("#txtName", (await session.SnapshotAsync()).Text, StringComparison.Ordinal);
        }
        finally
        {
            Assert.False(process.HasExited, "Disposing a session must not kill an attached app.");
            process.Kill();
        }
    }

    [Fact]
    [Trait("Category", "Build")]
    public async Task Launches_from_a_project_file()
    {
        Fixtures.KillStrays();
        var localSdk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet.exe");
        await using var session = new DesktopSession(new SessionOptions { DotnetExecutable = File.Exists(localSdk) ? localSdk : "dotnet" });
        var project = Path.Combine(Fixtures.RepositoryRoot, "tests", "fixtures", "Netwright.Fixtures.WinForms", "Netwright.Fixtures.WinForms.csproj");

        var status = await session.LaunchAsync(new LaunchRequest { Project = project, Framework = "net8.0-windows", Arguments = ["--no-activate"], TimeoutMs = 60000 });

        Assert.EndsWith("Netwright.Fixtures.WinForms.exe", status.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("WinForms", status.Framework);
    }

    [Fact]
    public async Task Works_with_net_framework_apps()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.WinFormsNetFramework);

        await session.TypeAsync("#txtName", new TypeRequest { Text = "Legacy" });
        await session.SetStateAsync("#chkTerms", new SetStateRequest { Checked = true });
        var clicked = await session.ClickAsync("#btnSubmit");

        Assert.Contains("Submitted: Legacy", clicked.Changes.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recorder_keeps_stable_selectors_for_test_export()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.Wpf);
        var snapshot = (await session.SnapshotAsync()).Text;

        await session.TypeAsync(RefOf(snapshot, "#txtName"), new TypeRequest { Text = "Ada" });
        await session.ClickAsync("checkbox \"Accept terms\"");
        await session.ExpectAsync("#btnSubmit", new ExpectRequest { Assertion = Assertion.Enabled });

        var steps = session.Recorder.Steps;
        Assert.Equal(["launch", "type", "click", "expect"], steps.Select(s => s.Kind));
        Assert.Equal("#txtName", steps[1].Selector);
        Assert.Equal("#chkTerms", steps[2].Selector);
        Assert.Equal("Ada", steps[1].Argument("text"));
    }

    [Fact]
    public async Task Cached_property_ids_match_their_ui_automation_names()
    {
        var automation = new Interop.UIAutomationClient.CUIAutomation8();

        var mismatches = PropertyIds.FullSet.Concat(PropertyIds.LightSet)
            .Select(p => (p.Id, Expected: p.ProgrammaticName, Actual: automation.GetPropertyProgrammaticName(p.Id)))
            .Where(p => p.Expected != p.Actual)
            .Select(p => $"{p.Id}: expected {p.Expected}, UIA says {p.Actual}")
            .ToList();

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
        await Task.CompletedTask;
    }

    private static string RefOf(string text, string automationId)
    {
        var match = Regex.Match(text, @"\[(?<ref>e\d+)\] " + Regex.Escape(automationId) + @"(\s|$)");
        Assert.True(match.Success, $"No ref for {automationId} in:{Environment.NewLine}{text}");
        return match.Groups["ref"].Value;
    }
}
