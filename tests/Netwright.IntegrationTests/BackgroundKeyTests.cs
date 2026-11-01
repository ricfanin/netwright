using Netwright.Engine;
using Netwright.Engine.Session;

namespace Netwright.IntegrationTests;

/// <summary>Key presses without the foreground, raised as WPF key events by the Companion (ADR 0009).</summary>
[Collection("UI")]
public sealed class BackgroundKeyTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Keys_reach_a_text_box_of_a_hidden_app_and_the_result_says_how()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.Wpf, "Keyboard");

        var pressed = await session.PressKeyAsync("#txtKeys", "Enter", foreground: false);
        output.WriteLine($"{pressed.Summary} | {pressed.Note}{Environment.NewLine}{pressed.Changes.Text}");

        Assert.EndsWith("(raised as WPF key events)", pressed.Summary, StringComparison.Ordinal);
        Assert.Contains("Last key: Return", pressed.Changes.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("took the foreground", pressed.Note ?? "", StringComparison.Ordinal);
        Assert.Equal("false", session.Recorder.Steps[^1].Argument("foreground"));

        var again = await session.PressKeyAsync("#txtKeys", "Escape", foreground: false);
        Assert.EndsWith("(raised as WPF key events)", again.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Character_keys_type_into_a_text_box()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.Wpf, "Keyboard");

        await session.PressKeyAsync("#txtKeys", "H I Space 2", foreground: false);

        await session.ExpectAsync("#txtKeys", new ExpectRequest { Assertion = Assertion.Value, Expected = "hi 2" });
    }

    [Fact]
    public async Task Arrow_keys_move_the_selection_in_a_tree()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.Wpf, "Tree");
        await session.SetStateAsync("treeitem \"Root\"", new SetStateRequest { Expanded = true });
        await session.SetStateAsync("treeitem \"Root\"", new SetStateRequest { Selected = true });

        var pressed = await session.PressKeyAsync("treeitem \"Root\"", "Down", foreground: false);
        output.WriteLine($"{pressed.Summary}{Environment.NewLine}{pressed.Changes.Text}");

        await session.ExpectAsync("#lblSelectedNode", new ExpectRequest { Assertion = Assertion.Text, Expected = "Selected: Documents" });
    }

    [Fact]
    public async Task A_key_binding_without_modifiers_runs_its_command()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.Wpf, "Keyboard");

        var targeted = await session.PressKeyAsync("#txtKeys", "F5", foreground: false);
        output.WriteLine($"{targeted.Summary}{Environment.NewLine}{targeted.Changes.Text}");
        await session.ExpectAsync("#lblCommand", new ExpectRequest { Assertion = Assertion.Text, Expected = "Command: refreshed 1" });

        var untargeted = await session.PressKeyAsync(null, "F5", foreground: false);
        output.WriteLine($"{untargeted.Summary}{Environment.NewLine}{untargeted.Changes.Text}");
        await session.ExpectAsync("#lblCommand", new ExpectRequest { Assertion = Assertion.Text, Expected = "Command: refreshed 2" });
    }

    [Fact]
    public async Task Chords_with_modifiers_still_need_the_foreground()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.Wpf, "Keyboard");

        foreach (var keys in new[] { "Ctrl+A", "Shift+F5", "Alt", "Enter Ctrl+S" })
        {
            var error = await Assert.ThrowsAsync<NetwrightException>(() => session.PressKeyAsync("#txtKeys", keys, foreground: false));
            Assert.Equal(ErrorCodes.NeedsForeground, error.Code);
        }

        await session.ExpectAsync("#lblLastKey", new ExpectRequest { Assertion = Assertion.Text, Expected = "Last key: none" });
    }

    [Fact]
    public async Task Apps_without_a_wpf_companion_still_need_the_foreground()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.WinForms, "Keyboard");

        var error = await Assert.ThrowsAsync<NetwrightException>(() => session.PressKeyAsync("#txtKeys", "Enter", foreground: false));

        Assert.Equal(ErrorCodes.NeedsForeground, error.Code);
        Assert.DoesNotContain(session.Recorder.Steps, s => s.Kind == "press_key");
    }

    [Fact]
    public async Task Keys_for_a_window_behind_an_open_modal_dialog_are_refused()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.Wpf);
        await session.ClickAsync("#btnOpenModal");

        var error = await Assert.ThrowsAsync<NetwrightException>(() => session.PressKeyAsync("#txtName", "A", foreground: false));
        output.WriteLine($"{error.Message} {error.Hint}");

        Assert.Equal(ErrorCodes.NotActionable, error.Code);
        Assert.Contains("modal dialog", error.Message, StringComparison.Ordinal);
    }
}
