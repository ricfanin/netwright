using System.Diagnostics;
using Netwright.Engine.Session;
using Netwright.Engine.Uia;

namespace Netwright.IntegrationTests;

/// <summary>
/// The Background promise: actions without foreground=true leave the User's foreground window alone
/// and still leave the Target App in the state a real User would (bindings committed).
/// </summary>
[Collection("UI")]
public sealed class BackgroundTests(ITestOutputHelper output)
{
    public static TheoryData<FixtureKind> Kinds => new() { FixtureKind.Wpf, FixtureKind.WinForms };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Typing_commits_bindings_that_wait_for_focus_to_leave_the_field(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);

        var typed = await session.TypeAsync("#txtBound", new TypeRequest { Text = "hello" });
        output.WriteLine(typed.Summary + " | note: " + typed.Note);

        Assert.Contains("committed its binding", typed.Summary, StringComparison.Ordinal);
        Assert.Null(typed.Note);
        await session.ExpectAsync("#lblBound", new ExpectRequest { Assertion = Assertion.Text, Expected = "Bound: hello" });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Setting_a_value_commits_bindings_too(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);

        await session.SetStateAsync("#txtBound", new SetStateRequest { Value = "set" });

        await session.ExpectAsync("#lblBound", new ExpectRequest { Assertion = Assertion.Text, Expected = "Bound: set" });
    }

    [Fact]
    public async Task An_attached_app_explains_once_that_bindings_may_not_be_committed()
    {
        Fixtures.KillStrays();
        using var process = Process.Start(new ProcessStartInfo(Fixtures.ExecutablePath(FixtureKind.Wpf), "--tab Form --no-activate") { UseShellExecute = false })!;
        try
        {
            await using var session = new DesktopSession();
            await session.AttachAsync(new AttachRequest { ProcessId = process.Id });

            var first = await session.TypeAsync("#txtBound", new TypeRequest { Text = "a" });
            var second = await session.TypeAsync("#txtName", new TypeRequest { Text = "b" });

            Assert.Contains("attached", first.Note, StringComparison.Ordinal);
            Assert.Contains("foreground=true", first.Note, StringComparison.Ordinal);
            Assert.DoesNotContain("Bindings", second.Note ?? "", StringComparison.Ordinal);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Background_actions_run_inside_the_app_and_never_hand_it_the_foreground(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);
        var status = await session.StatusAsync();

        // Checked against the Target App rather than an exact window: the User may switch windows meanwhile.
        async Task Check(string name, Func<Task<ActionOutcome>> action)
        {
            var outcome = await action();
            NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out var foregroundProcess);
            Assert.True(foregroundProcess != status.ProcessId, $"{name} left the Target App in the foreground.");
            Assert.DoesNotContain("took the foreground", outcome.Note ?? "", StringComparison.Ordinal);
        }

        await Check("type", () => session.TypeAsync("#txtName", new TypeRequest { Text = "Ada" }));
        await Check("type with binding", () => session.TypeAsync("#txtBound", new TypeRequest { Text = "bound" }));
        await Check("select item", () => session.SetStateAsync("#cmbCountry", new SetStateRequest { Item = "Italy" }));
        await Check("check", () => session.SetStateAsync("#chkTerms", new SetStateRequest { Checked = true }));
        await Check("select radio", () => session.SetStateAsync("#rbPlanPro", new SetStateRequest { Selected = true }));
        await Check("set range", () => session.SetStateAsync("#sldQuantity", new SetStateRequest { Value = "3" }));
        await Check("click", () => session.ClickAsync("#btnSubmit"));
        await session.ExpectAsync("#lblResult", new ExpectRequest { Assertion = Assertion.TextContains, Expected = "Submitted: Ada <> Italy Pro x3" });
    }

    [Fact]
    public async Task Without_the_companion_the_foreground_is_given_back()
    {
        Fixtures.KillStrays();
        using var process = Process.Start(new ProcessStartInfo(Fixtures.ExecutablePath(FixtureKind.Wpf), "--tab Form --no-activate") { UseShellExecute = false })!;
        try
        {
            await using var session = new DesktopSession();
            await session.AttachAsync(new AttachRequest { ProcessId = process.Id });

            await session.ClickAsync("#btnReset");

            NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out var foregroundProcess);
            Assert.NotEqual((uint)process.Id, foregroundProcess);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
