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
    public async Task A_depth_limited_full_snapshot_can_be_explored_top_down(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, "Grid");
        var whole = new Engine.Snapshots.SnapshotOptions { Full = true, MaxLines = 2000, MaxItemsPerContainer = int.MaxValue };
        var options = whole with { MaxDepth = 1 };

        var top = (await session.SnapshotAsync(null, options)).Text;
        output.WriteLine(top);
        var cut = Regex.Match(top, @"\.\.\. (?<n>\d+) more elements? below \(root=(?<ref>e\d+)\)");
        Assert.True(cut.Success, top);
        var all = (await session.SnapshotAsync(null, whole)).Text;
        Assert.True(Elements(top).Count() < Elements(all).Count(), "depth=1 should show fewer elements than the whole full Snapshot");

        var deeper = (await session.SnapshotAsync(cut.Groups["ref"].Value, options)).Text;
        output.WriteLine(deeper);
        var shown = Elements(top).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(Elements(deeper), e => !shown.Contains(e));

        // Element lines without indentation; summary lines ("... 3 more elements below") are left out.
        static IEnumerable<string> Elements(string snapshot) =>
            snapshot.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("- ", StringComparison.Ordinal) && !l.StartsWith("- ...", StringComparison.Ordinal));
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

    // WinForms tree items accept UIA SetFocus without taking focus: the tree keeps it and routes keys to its
    // selected item. A targeted press is therefore allowed there only on the selected item.
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Targeted_key_press_on_a_tree_item_moves_the_selection(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, "Tree");
        await session.SetStateAsync("treeitem \"Root\"", new SetStateRequest { Expanded = true });
        await session.SetStateAsync("treeitem \"Root\"", new SetStateRequest { Selected = true });

        var pressed = await session.PressKeyAsync("treeitem \"Root\"", "Down", foreground: true);
        output.WriteLine(pressed.Summary + Environment.NewLine + pressed.Changes.Text);

        await session.ExpectAsync("#lblSelectedNode", new ExpectRequest { Assertion = Assertion.Text, Expected = "Selected: Documents" });
    }

    // Without the focus check the keys went to the tree and moved the selection from Root, while the result
    // said they were pressed in Pictures.
    [Fact]
    public async Task Targeted_key_press_on_an_item_that_does_not_take_focus_is_refused()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.WinForms, "Tree");
        await session.SetStateAsync("treeitem \"Root\"", new SetStateRequest { Expanded = true });
        await session.SetStateAsync("treeitem \"Root\"", new SetStateRequest { Selected = true });

        var error = await Assert.ThrowsAsync<NetwrightException>(() => session.PressKeyAsync("treeitem \"Pictures\"", "Down", foreground: true));
        output.WriteLine($"{error.Code}: {error.Message} | {error.Hint}");

        Assert.Equal(ErrorCodes.NotActionable, error.Code);
        Assert.Contains("did not take keyboard focus", error.Message, StringComparison.Ordinal);
        await session.ExpectAsync("#lblSelectedNode", new ExpectRequest { Assertion = Assertion.Text, Expected = "Selected: Root" });
    }

    [Fact]
    public async Task Targeted_key_press_on_an_element_that_cannot_take_focus_is_not_an_internal_error()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.Wpf, "Tree");

        var error = await Assert.ThrowsAsync<NetwrightException>(() => session.PressKeyAsync("#lblSelectedNode", "Down", foreground: true));
        output.WriteLine($"{error.Code}: {error.Message} | {error.Hint}");

        Assert.Equal(ErrorCodes.NotActionable, error.Code);
        Assert.Contains("does not accept keyboard focus", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.Hint);
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
    public async Task Allow_list_is_checked_before_building_a_project()
    {
        await using var session = new DesktopSession(new SessionOptions { AllowedApps = ["notepad*"], DotnetExecutable = "does-not-exist.exe" });
        var project = Path.Combine(Fixtures.RepositoryRoot, "tests", "fixtures", "Netwright.Fixtures.Wpf", "Netwright.Fixtures.Wpf.csproj");

        var ex = await Assert.ThrowsAsync<NetwrightException>(() => session.LaunchAsync(new LaunchRequest { Project = project }));

        Assert.Equal(ErrorCodes.NotAllowed, ex.Code);

        // Without an SDK the project cannot be evaluated; the Agent learns that the file name stood in.
        Assert.Contains("could not be evaluated, so its file name was used", ex.Hint, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Build")]
    public async Task Allow_list_is_checked_against_the_evaluated_assembly_name()
    {
        // The name comes from Directory.Build.props and a property, as in many larger solutions.
        var directory = Directory.CreateTempSubdirectory("netwright-allow-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "Directory.Build.props"), "<Project><PropertyGroup><AssemblyName>Acme.$(MSBuildProjectName)</AssemblyName></PropertyGroup></Project>");
            var project = Path.Combine(directory, "Client.Shell.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net8.0-windows</TargetFramework></PropertyGroup></Project>");

            // A response file next to the project would turn the name into an allowed one; it must not be read.
            File.WriteAllText(Path.Combine(directory, "Directory.Build.rsp"), "-p:AssemblyName=Client.Shell");

            Assert.Equal((Path.Combine(directory, "Acme.Client.Shell.exe"), true), await ProjectBuilder.ExpectedExecutableAsync("dotnet", project, null, null, CancellationToken.None));

            // Matching only the project file name no longer passes the check, and nothing gets built.
            await using var session = new DesktopSession(new SessionOptions { AllowedApps = ["Client.Shell*"], DotnetExecutable = "dotnet" });
            var ex = await Assert.ThrowsAsync<NetwrightException>(() => session.LaunchAsync(new LaunchRequest { Project = project }));
            Assert.Equal(ErrorCodes.NotAllowed, ex.Code);
            Assert.Contains("Acme.Client.Shell.exe", ex.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(directory, "obj")), "The project must not be restored or built before the allow list check.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Build")]
    public async Task Evaluation_ignores_a_global_json_next_to_the_project()
    {
        // Since .NET 10, sdk.paths lets global.json point the dotnet host at an SDK folder beside it.
        // Run from the project folder, the host would load this dotnet.dll before the allow list check.
        var directory = Directory.CreateTempSubdirectory("netwright-allow-").FullName;
        try
        {
            var sdk = Directory.CreateDirectory(Path.Combine(directory, ".dotnet", "sdk", "10.0.100")).FullName;
            File.WriteAllText(Path.Combine(sdk, "dotnet.dll"), "not an SDK");
            File.WriteAllText(Path.Combine(directory, "global.json"), "{\"sdk\":{\"version\":\"10.0.100\",\"paths\":[\".dotnet\"]}}");
            var project = Path.Combine(directory, "Client.Shell.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net8.0-windows</TargetFramework><AssemblyName>Acme.Client.Shell</AssemblyName></PropertyGroup></Project>");

            Assert.Equal((Path.Combine(directory, "Acme.Client.Shell.exe"), true), await ProjectBuilder.ExpectedExecutableAsync("dotnet", project, null, null, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "Build")]
    public async Task Projects_on_a_unc_or_device_path_are_not_evaluated()
    {
        // \\?\UNC\host\share paths start the same way, so a local \\?\ path stands in for a share here.
        var directory = Directory.CreateTempSubdirectory("netwright-allow-").FullName;
        try
        {
            var project = Path.Combine(directory, "Client.Shell.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>WinExe</OutputType><TargetFramework>net8.0-windows</TargetFramework><AssemblyName>Acme.Client.Shell</AssemblyName></PropertyGroup></Project>");

            Assert.True((await ProjectBuilder.ExpectedExecutableAsync("dotnet", project, null, null, CancellationToken.None)).Evaluated);
            Assert.Equal((@"\\?\" + Path.Combine(directory, "Client.Shell.exe"), false), await ProjectBuilder.ExpectedExecutableAsync("dotnet", @"\\?\" + project, null, null, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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
