using System.Runtime.InteropServices;
using Netwright.Engine.Session;

namespace Netwright.IntegrationTests;

/// <summary>
/// Apps launched with the Companion run hidden by default: their windows are cloaked, never shown,
/// and still fully operable (ADR 0008).
/// </summary>
[Collection("UI")]
public sealed class HiddenTests
{
    public static TheoryData<FixtureKind> Kinds => new() { FixtureKind.Wpf, FixtureKind.WinForms };

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Launched_apps_are_hidden_yet_operable(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind);

        var status = await session.StatusAsync();
        Assert.True(status.Hidden);
        Assert.All(TopLevelWindows(status.ProcessId), window => Assert.True(IsHidden(window)));

        await session.TypeAsync("#txtName", new TypeRequest { Text = "Ada" });
        await session.SetStateAsync("#chkTerms", new SetStateRequest { Checked = true });
        await session.ClickAsync("#btnSubmit");
        await session.ExpectAsync("#lblResult", new ExpectRequest { Assertion = Assertion.TextContains, Expected = "Submitted: Ada" });

        var screenshot = await session.ScreenshotAsync();
        Assert.True(screenshot.Image.Png.Length > 5000, "A hidden window still renders for screenshots.");
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Foreground_actions_show_the_window_only_while_they_run(FixtureKind kind)
    {
        await using var session = await Fixtures.LaunchAsync(kind, "Keyboard");
        var status = await session.StatusAsync();

        await session.PressKeyAsync("#txtKeys", "Enter", foreground: true);

        await session.ExpectAsync("#lblLastKey", new ExpectRequest { Assertion = Assertion.TextContains, Expected = "Last key: " });
        Assert.All(TopLevelWindows(status.ProcessId), window => Assert.True(IsHidden(window)));
    }

    [Fact]
    public async Task Visible_launches_are_not_cloaked()
    {
        Fixtures.KillStrays();
        await using var session = new DesktopSession();
        var status = await session.LaunchAsync(new LaunchRequest
        {
            Path = Fixtures.ExecutablePath(FixtureKind.Wpf),
            Arguments = ["--tab", "Form", "--no-activate"],
            Visible = true,
        });

        Assert.False(status.Hidden);
        Assert.All(TopLevelWindows(status.ProcessId), window => Assert.False(IsHidden(window)));
    }

    private static List<nint> TopLevelWindows(int processId)
    {
        var windows = new List<nint>();
        EnumWindows((window, _) =>
        {
            if (IsWindowVisible(window) && GetWindowThreadProcessId(window, out var owner) != 0 && owner == processId)
            {
                windows.Add(window);
            }

            return true;
        }, 0);
        Assert.NotEmpty(windows);
        return windows;
    }

    // Hidden means cloaked (WPF) or layered with zero opacity (WinForms).
    private static bool IsHidden(nint window) =>
        (DwmGetWindowAttribute(window, 14 /* DWMWA_CLOAKED */, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
        || (GetLayeredWindowAttributes(window, out _, out var alpha, out var flags) && (flags & 0x2) != 0 && alpha == 0);

    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetLayeredWindowAttributes(nint window, out uint key, out byte alpha, out uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);
}
