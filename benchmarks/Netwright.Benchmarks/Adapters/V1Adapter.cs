using System.Text.Json;
using System.Text.RegularExpressions;

namespace Netwright.Benchmarks.Adapters;

/// <summary>
/// WPF-MCP 1.x: <c>wpf_*</c> tools, JSON envelopes, YAML snapshots scoped to refs that are
/// invalidated by every snapshot.
/// </summary>
public sealed partial class V1Adapter : IServerAdapter
{
    private const string FormName = "Ada Lovelace";
    private const string FormEmail = "ada@example.com";

    public string Name => "WPF-MCP 1.x";

    public async Task InitializeAsync(McpSession session)
    {
        // Without background mode v1 types through the real keyboard and steals focus.
        await session.CallAsync("wpf_set_background_mode", new { enabled = true }, record: false);
    }

    public async Task LaunchFixtureAsync(McpSession session, string fixturePath, string tab)
    {
        var result = await session.CallAsync(
            "wpf_launch_application",
            new { path = fixturePath, arguments = new[] { "--tab", tab, "--no-activate" } },
            record: false);

        if (result.IsError)
        {
            throw new InvalidOperationException($"v1 failed to launch fixture: {result.Text}");
        }
    }

    public Task CloseFixtureAsync(McpSession session) =>
        session.CallAsync("wpf_close_application", new { force = true }, record: false);

    public async Task<bool> SnapshotAsync(McpSession session)
    {
        var yaml = await SnapshotUntilAsync(session, y => y.Contains("[ref=", StringComparison.Ordinal));
        return yaml is not null;
    }

    public async Task<bool> FormSubmitAsync(McpSession session)
    {
        var yaml = await SnapshotUntilAsync(session, y => FindRef(y, "button", "Submit") is not null);
        if (yaml is null)
        {
            return false;
        }

        var nameRef = FindRef(yaml, "edit", "Name");
        var emailRef = FindRef(yaml, "edit", "Email");
        var countryRef = FindRef(yaml, "combobox", "Country");
        var termsRef = FindRef(yaml, "checkbox", "Accept terms");
        var submitRef = FindRef(yaml, "button", "Submit");
        if (nameRef is null || emailRef is null || countryRef is null || termsRef is null || submitRef is null)
        {
            return false;
        }

        await session.CallAsync("wpf_type", new { element = "Name field", @ref = nameRef, text = FormName });
        await session.CallAsync("wpf_type", new { element = "Email field", @ref = emailRef, text = FormEmail });

        var select = await session.CallAsync("wpf_select", new { element = "Country combo", @ref = countryRef, item = "Italy" });
        if (select.IsError)
        {
            // Collapsed combo boxes expose no items until opened; an Agent expands and retries.
            await session.CallAsync("wpf_expand_collapse", new { element = "Country combo", @ref = countryRef, action = "expand" });
            select = await session.CallAsync("wpf_select", new { element = "Country combo", @ref = countryRef, item = "Italy" });
            await session.CallAsync("wpf_expand_collapse", new { element = "Country combo", @ref = countryRef, action = "collapse" });
        }

        await session.CallAsync("wpf_toggle", new { element = "Accept terms", @ref = termsRef, target_state = "on" });
        var click = await session.CallAsync("wpf_click", new { element = "Submit button", @ref = submitRef });
        if (click.IsError || select.IsError)
        {
            return false;
        }

        var after = await SnapshotUntilAsync(session, y => y.Contains("Submitted:", StringComparison.Ordinal), maxAttempts: 3);
        return after is not null && after.Contains($"Submitted: {FormName}", StringComparison.Ordinal);
    }

    public async Task<bool> AsyncLoadAsync(McpSession session)
    {
        var yaml = await SnapshotUntilAsync(session, y => FindRef(y, "button", "Load data") is not null);
        var loadRef = yaml is null ? null : FindRef(yaml, "button", "Load data");
        if (loadRef is null)
        {
            return false;
        }

        var click = await session.CallAsync("wpf_click", new { element = "Load data button", @ref = loadRef });
        if (click.IsError)
        {
            return false;
        }

        // v1 can only wait on element states, not on text, so the Agent polls with snapshots.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(500);
            var polled = await SnapshotAsyncRaw(session, maxDepth: 20);
            if (polled?.Contains("Loaded 3 items", StringComparison.Ordinal) == true)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<bool> ScreenshotAsync(McpSession session)
    {
        var result = await session.CallAsync("wpf_take_screenshot", new { format = "png" });
        return !result.IsError;
    }

    /// <summary>
    /// Takes the default-depth snapshot first, like an Agent would, and only retries deeper when the
    /// expected content is missing.
    /// </summary>
    private static async Task<string?> SnapshotUntilAsync(McpSession session, Func<string, bool> predicate, int maxAttempts = 2)
    {
        int[] depths = maxAttempts <= 2 ? [5, 20] : [20, 20, 20];
        foreach (var depth in depths.Take(maxAttempts))
        {
            var yaml = await SnapshotAsyncRaw(session, depth);
            if (yaml is not null && predicate(yaml))
            {
                return yaml;
            }
        }

        return null;
    }

    private static async Task<string?> SnapshotAsyncRaw(McpSession session, int maxDepth)
    {
        var result = await session.CallAsync("wpf_snapshot", new { max_depth = maxDepth });
        if (result.IsError)
        {
            return null;
        }

        using var document = JsonDocument.Parse(result.Text);
        return document.RootElement.GetProperty("data").GetProperty("snapshot").GetString();
    }

    private static string? FindRef(string yaml, string controlType, string name)
    {
        foreach (Match match in SnapshotLine().Matches(yaml))
        {
            if (string.Equals(match.Groups["type"].Value, controlType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(match.Groups["name"].Value, name, StringComparison.Ordinal))
            {
                return match.Groups["ref"].Value;
            }
        }

        return null;
    }

    [GeneratedRegex("""- (?<type>\S+)(?: "(?<name>[^"]*)")? \[ref=(?<ref>e\d+)\]""")]
    private static partial Regex SnapshotLine();
}
