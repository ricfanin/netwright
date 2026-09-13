using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Netwright.IntegrationTests;

/// <summary>
/// Drives the real <c>netwright</c> executable over stdio, the way an MCP client does.
/// Build it first: dotnet publish src/Netwright -c Release -r win-x64 -o artifacts/netwright
/// </summary>
[Collection("UI")]
public sealed class McpServerTests(ITestOutputHelper output) : IAsyncLifetime
{
    private McpClient? _client;

    public async Task InitializeAsync()
    {
        Fixtures.KillStrays();
        var server = Path.Combine(Fixtures.RepositoryRoot, "artifacts", "netwright", "netwright.exe");
        if (!File.Exists(server))
        {
            throw new InvalidOperationException($"Server not published at {server}. Run: dotnet publish src/Netwright -c Release -r win-x64 -o artifacts/netwright");
        }

        _client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "netwright",
            Command = server,
            Arguments = ["--action-timeout", "1500"],
        }));
    }

    public async Task DisposeAsync()
    {
        if (_client is not null)
        {
            await _client.DisposeAsync();
        }

        Fixtures.KillStrays();
    }

    [Fact]
    public async Task Exposes_the_desktop_tools_with_annotations()
    {
        var tools = await _client!.ListToolsAsync();
        var names = tools.Select(t => t.ProtocolTool.Name).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(
            ["desktop_app", "desktop_click", "desktop_expect", "desktop_export_test", "desktop_find", "desktop_inspect", "desktop_logs", "desktop_press_key",
             "desktop_screenshot", "desktop_scroll", "desktop_set_state", "desktop_snapshot", "desktop_type", "desktop_wait", "desktop_window"],
            names);

        foreach (var tool in tools)
        {
            var schema = tool.ProtocolTool.InputSchema.GetRawText();
            Assert.DoesNotContain("\"null\"", schema, StringComparison.Ordinal);
            Assert.DoesNotContain("\"default\":null", schema, StringComparison.Ordinal);
        }

        var snapshot = tools.Single(t => t.ProtocolTool.Name == "desktop_snapshot").ProtocolTool;
        Assert.True(snapshot.Annotations?.ReadOnlyHint);
        var click = tools.Single(t => t.ProtocolTool.Name == "desktop_click").ProtocolTool;
        Assert.NotEqual(true, click.Annotations?.ReadOnlyHint);
        Assert.False(string.IsNullOrWhiteSpace(_client.ServerInstructions));
    }

    [Fact]
    public async Task Launch_snapshot_act_and_close_through_mcp()
    {
        var launched = await Call("desktop_app", new { action = "launch", path = Fixtures.ExecutablePath(FixtureKind.Wpf), args = new[] { "--no-activate" } });
        Assert.StartsWith("Launched Netwright.Fixtures.Wpf.exe", launched.Text, StringComparison.Ordinal);

        var snapshot = await Call("desktop_snapshot");
        Assert.Contains("#txtName", snapshot.Text, StringComparison.Ordinal);

        var typed = await Call("desktop_type", new { target = "#txtName", text = "Ada" });
        Assert.Contains("value=\"Ada\"", typed.Text, StringComparison.Ordinal);
        Assert.True(typed.Structured.GetProperty("ok").GetBoolean());
        Assert.True(typed.Structured.GetProperty("settled").GetBoolean());

        var expect = await Call("desktop_expect", new { target = "#txtName", assertion = "text", expected = "Ada" });
        Assert.StartsWith("pass:", expect.Text, StringComparison.Ordinal);

        var closed = await Call("desktop_app", new { action = "close" });
        Assert.StartsWith("Closed", closed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Errors_are_tool_results_with_code_and_hint()
    {
        var noApp = await Call("desktop_snapshot", expectError: true);
        Assert.StartsWith("error NO_APP:", noApp.Text, StringComparison.Ordinal);
        Assert.Contains("hint:", noApp.Text, StringComparison.Ordinal);
        Assert.Equal("NO_APP", noApp.Structured.GetProperty("code").GetString());

        await Call("desktop_app", new { action = "launch", path = Fixtures.ExecutablePath(FixtureKind.WinForms), args = new[] { "--no-activate" } });
        var badEnum = await Call("desktop_click", new { target = "#btnReset", button = "sideways" }, expectError: true);
        Assert.Contains("INVALID_ARGUMENT", badEnum.Text, StringComparison.Ordinal);

        var disabled = await Call("desktop_click", new { target = "#btnSubmit" }, expectError: true);
        Assert.StartsWith("error NOT_ACTIONABLE:", disabled.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Screenshots_are_image_content()
    {
        await Call("desktop_app", new { action = "launch", path = Fixtures.ExecutablePath(FixtureKind.Wpf), args = new[] { "--no-activate" } });

        var result = await _client!.CallToolAsync("desktop_screenshot", new Dictionary<string, object?> { ["max_size"] = 640 });

        var image = Assert.Single(result.Content.OfType<ImageContentBlock>());
        Assert.Equal("image/png", image.MimeType);
        Assert.Contains("640x", Assert.Single(result.Content.OfType<TextContentBlock>()).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Crash_reports_arrive_with_the_next_tool_call()
    {
        await Call("desktop_app", new { action = "launch", path = Fixtures.ExecutablePath(FixtureKind.Wpf), args = new[] { "--no-activate" } });

        // The app usually dies while the click waits for the UI to settle, so the report is attached
        // to the click's own response; otherwise it arrives with the next call.
        var click = await Call("desktop_click", new { target = "#btnCrash" });
        var report = click.Text;
        if (!report.StartsWith("! Target App exited unexpectedly", StringComparison.Ordinal))
        {
            await Task.Delay(1000);
            report = (await Call("desktop_snapshot", expectError: true)).Text;
        }

        output.WriteLine(report);
        Assert.StartsWith("! Target App exited unexpectedly", report, StringComparison.Ordinal);
        Assert.Contains("Fixture crash requested", report, StringComparison.Ordinal);
    }

    private async Task<(string Text, JsonElement Structured)> Call(string tool, object? arguments = null, bool expectError = false)
    {
        var args = arguments is null
            ? []
            : JsonSerializer.SerializeToElement(arguments).EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);

        var result = await _client!.CallToolAsync(tool, args);
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));
        output.WriteLine($"> {tool}: {text}");
        Assert.True(expectError == (result.IsError == true), $"{tool} returned isError={result.IsError}: {text}");

        var structured = result.StructuredContent is { } element ? JsonSerializer.SerializeToElement(element) : default;
        return (text, structured);
    }
}
