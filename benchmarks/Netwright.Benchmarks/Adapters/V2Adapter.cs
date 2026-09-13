using System.Text.RegularExpressions;

namespace Netwright.Benchmarks.Adapters;

/// <summary>
/// Netwright 2.x: <c>desktop_*</c> tools, compact text, stable Refs, and Change Reports that make
/// follow-up snapshots unnecessary.
/// </summary>
public sealed partial class V2Adapter : IServerAdapter
{
    private const string FormName = "Ada Lovelace";
    private const string FormEmail = "ada@example.com";

    public string Name => "Netwright 2.x";

    public Task InitializeAsync(McpSession session) => Task.CompletedTask;

    public async Task LaunchFixtureAsync(McpSession session, string fixturePath, string tab)
    {
        var result = await session.CallAsync(
            "desktop_app",
            new { action = "launch", path = fixturePath, args = new[] { "--tab", tab, "--no-activate" } },
            record: false);

        if (result.IsError)
        {
            throw new InvalidOperationException($"Netwright failed to launch fixture: {result.Text}");
        }
    }

    public Task CloseFixtureAsync(McpSession session) =>
        session.CallAsync("desktop_app", new { action = "close", force = true }, record: false);

    public async Task<bool> SnapshotAsync(McpSession session)
    {
        var result = await session.CallAsync("desktop_snapshot");
        return !result.IsError && result.Text.Contains("[e", StringComparison.Ordinal);
    }

    public async Task<bool> FormSubmitAsync(McpSession session)
    {
        var snapshot = await session.CallAsync("desktop_snapshot");
        var nameRef = FindRef(snapshot.Text, "textbox", "Name");
        var emailRef = FindRef(snapshot.Text, "textbox", "Email");
        var countryRef = FindRef(snapshot.Text, "combobox", "Country");
        var termsRef = FindRef(snapshot.Text, "checkbox", "Accept terms");
        var submitRef = FindRef(snapshot.Text, "button", "Submit");
        if (nameRef is null || emailRef is null || countryRef is null || termsRef is null || submitRef is null)
        {
            return false;
        }

        var steps = new[]
        {
            await session.CallAsync("desktop_type", new { target = nameRef, text = FormName }),
            await session.CallAsync("desktop_type", new { target = emailRef, text = FormEmail }),
            await session.CallAsync("desktop_set_state", new { target = countryRef, item = "Italy" }),
            await session.CallAsync("desktop_set_state", new { target = termsRef, @checked = true }),
        };

        var click = await session.CallAsync("desktop_click", new { target = submitRef });
        return steps.All(s => !s.IsError) && !click.IsError && click.Text.Contains($"Submitted: {FormName}", StringComparison.Ordinal);
    }

    public async Task<bool> AsyncLoadAsync(McpSession session)
    {
        var snapshot = await session.CallAsync("desktop_snapshot");
        var loadRef = FindRef(snapshot.Text, "button", "Load data");
        if (loadRef is null)
        {
            return false;
        }

        var click = await session.CallAsync("desktop_click", new { target = loadRef });
        if (click.IsError)
        {
            return false;
        }

        if (click.Text.Contains("Loaded 3 items", StringComparison.Ordinal))
        {
            return true;
        }

        var wait = await session.CallAsync("desktop_wait", new { text = "Loaded 3 items", timeout_ms = 5000 });
        return !wait.IsError;
    }

    public async Task<bool> ScreenshotAsync(McpSession session)
    {
        var result = await session.CallAsync("desktop_screenshot");
        return !result.IsError;
    }

    private static string? FindRef(string snapshot, string role, string name)
    {
        foreach (Match match in SnapshotLine().Matches(snapshot))
        {
            if (match.Groups["role"].Value == role && string.Equals(match.Groups["name"].Value, name, StringComparison.Ordinal))
            {
                return match.Groups["ref"].Value;
            }
        }

        return null;
    }

    [GeneratedRegex("""[-+~] (?<role>[a-z]+) "(?<name>(?:[^"\\]|\\.)*)" \[(?<ref>e\d+)\]""")]
    private static partial Regex SnapshotLine();
}
