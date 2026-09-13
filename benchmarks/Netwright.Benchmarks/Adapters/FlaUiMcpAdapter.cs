using System.Text.RegularExpressions;

namespace Netwright.Benchmarks.Adapters;

/// <summary>
/// FlaUI-MCP (github.com/shanselman/FlaUI-MCP): <c>windows_*</c> tools, snapshots scoped to a window
/// handle, refs like <c>w1e5</c>. Driven the way its README recommends.
/// </summary>
public sealed partial class FlaUiMcpAdapter : IServerAdapter
{
    private const string FormName = "Ada Lovelace";
    private const string FormEmail = "ada@example.com";

    private string? _handle;

    public string Name => "FlaUI-MCP";

    public Task InitializeAsync(McpSession session) => Task.CompletedTask;

    public async Task LaunchFixtureAsync(McpSession session, string fixturePath, string tab)
    {
        var result = await session.CallAsync("windows_launch", new { app = fixturePath, args = new[] { "--tab", tab, "--no-activate" } }, record: false);
        var match = HandleLine().Match(result.Text);
        if (result.IsError || !match.Success)
        {
            throw new InvalidOperationException($"FlaUI-MCP failed to launch fixture: {result.Text}");
        }

        _handle = match.Groups["handle"].Value;
    }

    public async Task CloseFixtureAsync(McpSession session)
    {
        if (_handle is not null)
        {
            await session.CallAsync("windows_close", new { handle = _handle }, record: false);
        }
    }

    public async Task<bool> SnapshotAsync(McpSession session)
    {
        var result = await session.CallAsync("windows_snapshot", new { handle = _handle });
        return !result.IsError && result.Text.Contains("[ref=", StringComparison.Ordinal);
    }

    public async Task<bool> FormSubmitAsync(McpSession session)
    {
        var snapshot = (await session.CallAsync("windows_snapshot", new { handle = _handle })).Text;
        var nameRef = FindRef(snapshot, "edit", "Name");
        var emailRef = FindRef(snapshot, "edit", "Email");
        var countryRef = FindRef(snapshot, "combobox", "Country");
        var termsRef = FindRef(snapshot, "checkbox", "Accept terms");
        if (nameRef is null || emailRef is null || countryRef is null || termsRef is null)
        {
            return false;
        }

        await session.CallAsync("windows_fill", new { @ref = nameRef, value = FormName });
        await session.CallAsync("windows_fill", new { @ref = emailRef, value = FormEmail });

        // No selection tool: open the combo box, find the item in a new snapshot, click it.
        await session.CallAsync("windows_click", new { @ref = countryRef });
        var opened = (await session.CallAsync("windows_snapshot", new { handle = _handle })).Text;
        if (FindRef(opened, "listitem", "Italy") is { } italyRef)
        {
            await session.CallAsync("windows_click", new { @ref = italyRef });
        }

        await session.CallAsync("windows_click", new { @ref = termsRef });

        // Refs may have been reassigned by the intermediate snapshot.
        var ready = (await session.CallAsync("windows_snapshot", new { handle = _handle })).Text;
        if (FindRef(ready, "button", "Submit") is not { } submitRef)
        {
            return false;
        }

        await session.CallAsync("windows_click", new { @ref = submitRef });
        var after = await session.CallAsync("windows_snapshot", new { handle = _handle });
        return after.Text.Contains($"Submitted: {FormName} <{FormEmail}> Italy", StringComparison.Ordinal);
    }

    public async Task<bool> AsyncLoadAsync(McpSession session)
    {
        var snapshot = (await session.CallAsync("windows_snapshot", new { handle = _handle })).Text;
        if (FindRef(snapshot, "button", "Load data") is not { } loadRef)
        {
            return false;
        }

        await session.CallAsync("windows_click", new { @ref = loadRef });
        for (var attempt = 0; attempt < 10; attempt++)
        {
            await Task.Delay(500);
            var polled = await session.CallAsync("windows_snapshot", new { handle = _handle });
            if (polled.Text.Contains("Loaded 3 items", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public async Task<bool> ScreenshotAsync(McpSession session)
    {
        var result = await session.CallAsync("windows_screenshot", new { handle = _handle });
        return !result.IsError;
    }

    private static string? FindRef(string snapshot, string role, string name)
    {
        foreach (Match match in SnapshotLine().Matches(snapshot))
        {
            if (string.Equals(match.Groups["role"].Value, role, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(match.Groups["name"].Value, name, StringComparison.Ordinal))
            {
                return match.Groups["ref"].Value;
            }
        }

        return null;
    }

    [GeneratedRegex(@"Window handle: (?<handle>\S+)")]
    private static partial Regex HandleLine();

    [GeneratedRegex("""- (?<role>\S+)(?: "(?<name>[^"]*)")?.*?\[ref=(?<ref>w\d+e\d+|w\d+)\]""")]
    private static partial Regex SnapshotLine();
}
