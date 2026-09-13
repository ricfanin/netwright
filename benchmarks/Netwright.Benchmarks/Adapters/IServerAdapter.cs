namespace Netwright.Benchmarks.Adapters;

/// <summary>
/// Drives one MCP server through the Benchmark scenarios the way an Agent would use that
/// server's tools. Setup (launching and closing the Fixture App) is not recorded; every call made
/// inside a scenario is.
/// </summary>
public interface IServerAdapter
{
    string Name { get; }

    /// <summary>
    /// When true, response texts are not stored in result files. Use it for servers whose output
    /// includes the whole desktop (other windows of the machine running the Benchmark).
    /// </summary>
    bool RedactTranscripts => false;

    /// <summary>Scenarios the server cannot perform with its tools are skipped rather than counted as failures.</summary>
    bool Supports(string scenario) => true;

    /// <summary>One-time session setup, not recorded.</summary>
    Task InitializeAsync(McpSession session);

    Task LaunchFixtureAsync(McpSession session, string fixturePath, string tab);

    Task CloseFixtureAsync(McpSession session);

    /// <summary>Reads the current screen once.</summary>
    Task<bool> SnapshotAsync(McpSession session);

    /// <summary>Fills and submits the Form tab, then confirms the result text.</summary>
    Task<bool> FormSubmitAsync(McpSession session);

    /// <summary>Starts the async load on the Async tab and confirms it completed.</summary>
    Task<bool> AsyncLoadAsync(McpSession session);

    /// <summary>Captures the main window as an image.</summary>
    Task<bool> ScreenshotAsync(McpSession session);
}
