namespace Netwright.Benchmarks.Adapters;

/// <summary>
/// Windows-MCP (github.com/CursorTouch/Windows-MCP): whole-desktop snapshots with numbered labels and
/// coordinates, and mouse/keyboard input at those coordinates. Only the read-only scenarios are
/// measured: its actions move the real cursor, and its output describes every window on the desktop,
/// so transcripts are not stored.
/// </summary>
public sealed class WindowsMcpAdapter : IServerAdapter
{
    public string Name => "Windows-MCP";

    public bool RedactTranscripts => true;

    public bool Supports(string scenario) => scenario is "snapshot-form" or "snapshot-grid" or "screenshot";

    public Task InitializeAsync(McpSession session) => Task.CompletedTask;

    public async Task LaunchFixtureAsync(McpSession session, string fixturePath, string tab)
    {
        var result = await session.CallAsync("App", new { mode = "launch_executable", executable = fixturePath, args = new[] { "--tab", tab } }, record: false);
        if (result.IsError)
        {
            throw new InvalidOperationException($"Windows-MCP failed to launch fixture: {result.Text}");
        }

        await Task.Delay(1500); // Windows-MCP returns before the window exists.
    }

    // The runner kills the fixture process after each iteration.
    public Task CloseFixtureAsync(McpSession session) => Task.CompletedTask;

    public async Task<bool> SnapshotAsync(McpSession session)
    {
        var result = await session.CallAsync("Snapshot", new { use_vision = false });
        return !result.IsError && result.Text.Contains("Netwright Fixture", StringComparison.Ordinal);
    }

    public Task<bool> FormSubmitAsync(McpSession session) => Task.FromResult(false);

    public Task<bool> AsyncLoadAsync(McpSession session) => Task.FromResult(false);

    public async Task<bool> ScreenshotAsync(McpSession session)
    {
        var result = await session.CallAsync("Screenshot");
        return !result.IsError;
    }
}
