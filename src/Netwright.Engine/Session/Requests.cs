using Netwright.Engine.Capture;
using Netwright.Engine.Snapshots;

namespace Netwright.Engine.Session;

public sealed record SessionOptions
{
    /// <summary>Wildcard patterns (<c>*</c>, <c>?</c>) matched against executable paths and names. Empty allows every app.</summary>
    public IReadOnlyList<string> AllowedApps { get; init; } = [];

    /// <summary>How long an action waits for its element to exist and be Actionable.</summary>
    public int ActionTimeoutMs { get; init; } = 5000;

    /// <summary>Upper bound for waiting until the Target App is Settled after an action.</summary>
    public int SettleTimeoutMs { get; init; } = 3000;

    /// <summary>The UI must stay unchanged for this long after an action to count as Settled.</summary>
    public int SettleQuietMs { get; init; } = 150;

    /// <summary>
    /// A UI Automation call that has not returned after this long is assumed to have opened modal UI
    /// (for example a dialog shown from a click handler); the action continues without waiting for it.
    /// </summary>
    public int BlockingCallTimeoutMs { get; init; } = 1500;

    public SnapshotOptions Snapshot { get; init; } = new();

    public int ChangeReportMaxLines { get; init; } = 40;

    /// <summary>The .NET SDK host used to build projects.</summary>
    public string DotnetExecutable { get; init; } = Environment.GetEnvironmentVariable("NETWRIGHT_DOTNET") ?? "dotnet";
}

public sealed record LaunchRequest
{
    public string? Path { get; init; }

    public string? Project { get; init; }

    public IReadOnlyList<string> Arguments { get; init; } = [];

    public string? WorkingDirectory { get; init; }

    public string? Configuration { get; init; }

    public string? Framework { get; init; }

    public int TimeoutMs { get; init; } = 30000;
}

public sealed record AttachRequest
{
    public int? ProcessId { get; init; }

    public string? ProcessName { get; init; }

    public string? WindowTitle { get; init; }

    public int TimeoutMs { get; init; } = 10000;
}

public sealed record AppStatus(
    int ProcessId,
    string ExecutablePath,
    bool LaunchedBySession,
    string Framework,
    IReadOnlyList<string> WindowLines,
    string? DebugCaptureWarning);

public enum MouseButtonKind
{
    Left,
    Right,
    Middle,
}

public sealed record ClickRequest
{
    public MouseButtonKind Button { get; init; } = MouseButtonKind.Left;

    public int ClickCount { get; init; } = 1;

    public bool Foreground { get; init; }
}

public sealed record TypeRequest
{
    public required string Text { get; init; }

    public bool Clear { get; init; } = true;

    /// <summary>Press Enter after typing (needs a Foreground Action).</summary>
    public bool Submit { get; init; }

    public bool Foreground { get; init; }
}

public sealed record SetStateRequest
{
    public bool? Checked { get; init; }

    public bool? Selected { get; init; }

    public bool? Expanded { get; init; }

    public string? Value { get; init; }

    /// <summary>Name of a child item to select in a list, combo box, tab control or tree.</summary>
    public string? Item { get; init; }
}

public enum ScrollDirection
{
    Up,
    Down,
    Left,
    Right,
}

public enum ScrollAmountKind
{
    Small,
    Page,
}

public enum ScrollEdge
{
    Top,
    Bottom,
    Start,
    End,
}

public sealed record ScrollRequest
{
    public ScrollDirection? Direction { get; init; }

    public ScrollAmountKind Amount { get; init; } = ScrollAmountKind.Page;

    public ScrollEdge? To { get; init; }

    /// <summary>Scroll the target element itself into view instead of scrolling a container.</summary>
    public bool IntoView { get; init; }
}

public enum WindowAction
{
    List,
    Activate,
    Minimize,
    Maximize,
    Restore,
    Close,
}

public enum ElementState
{
    Visible,
    Hidden,
    Enabled,
    Disabled,
    Exists,
    Gone,
}

public sealed record WaitRequest
{
    public string? Target { get; init; }

    public ElementState State { get; init; } = ElementState.Visible;

    /// <summary>Text (name or value) that must appear anywhere in the Target App, or disappear when State is Gone/Hidden.</summary>
    public string? Text { get; init; }

    public int TimeoutMs { get; init; } = 10000;
}

public enum Assertion
{
    Exists,
    Gone,
    Visible,
    Hidden,
    Enabled,
    Disabled,
    Checked,
    Unchecked,
    Selected,
    NotSelected,
    Expanded,
    Collapsed,
    Focused,
    Text,
    TextContains,
    Value,
    Count,
}

public sealed record ExpectRequest
{
    public required Assertion Assertion { get; init; }

    public string? Expected { get; init; }

    public int TimeoutMs { get; init; } = 2000;
}

public sealed record ActionOutcome(string Summary, ChangeReport Changes, bool Settled, long ElapsedMs, string? Note = null);

public sealed record SnapshotOutcome(string Text, int Lines, int WindowCount);

public sealed record ScreenshotOutcome(CapturedImage Image, string Summary);

public sealed record LogsOutcome(IReadOnlyList<Diagnostics.OutputEntry> Entries, int Skipped, string? Warning);
