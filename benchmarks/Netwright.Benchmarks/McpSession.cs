using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Netwright.Benchmarks;

/// <summary>One tool call as the Agent would experience it.</summary>
public sealed record CallRecord(string Tool, int ResponseTokens, double LatencyMs, bool IsError, string Text);

/// <summary>
/// Wraps an MCP stdio client and records the token cost and latency of every tool call.
/// </summary>
public sealed class McpSession : IAsyncDisposable
{
    private readonly McpClient _client;
    private readonly List<CallRecord> _calls = [];

    private McpSession(McpClient client) => _client = client;

    public IReadOnlyList<CallRecord> Calls => _calls;

    public static async Task<McpSession> StartAsync(ServerDefinition server, CancellationToken cancellationToken = default)
    {
        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = server.Name,
            Command = server.Command,
            Arguments = server.Arguments,
            WorkingDirectory = server.WorkingDirectory,
            EnvironmentVariables = server.Environment,
        });

        // Some servers only speak the stateless 2026-07-28 protocol and reject the initialize handshake.
        var options = server.ProtocolVersion is null ? null : new McpClientOptions { ProtocolVersion = server.ProtocolVersion };
        var client = await McpClient.CreateAsync(transport, options, cancellationToken: cancellationToken);
        return new McpSession(client);
    }

    /// <summary>Returns the tool definitions exactly as a client forwards them to the model.</summary>
    public async Task<ToolDefinitionStats> MeasureToolDefinitionsAsync(CancellationToken cancellationToken = default)
    {
        var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);
        var perTool = new List<(string Name, int Tokens)>();

        foreach (var tool in tools)
        {
            var definition = JsonSerializer.Serialize(new
            {
                name = tool.ProtocolTool.Name,
                description = tool.ProtocolTool.Description,
                input_schema = tool.ProtocolTool.InputSchema,
            });
            perTool.Add((tool.ProtocolTool.Name, TokenCounter.Count(definition)));
        }

        return new ToolDefinitionStats(perTool.Count, perTool.Sum(t => t.Tokens), perTool.ToDictionary(t => t.Name, t => t.Tokens));
    }

    public async Task<IReadOnlyList<string>> ToolDefinitionsJsonAsync(CancellationToken cancellationToken = default)
    {
        var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);
        return tools.Select(t => JsonSerializer.Serialize(new { name = t.ProtocolTool.Name, description = t.ProtocolTool.Description, input_schema = t.ProtocolTool.InputSchema })).ToList();
    }

    public async Task<IReadOnlyList<string>> ListToolNamesAsync(CancellationToken cancellationToken = default)
    {
        var tools = await _client.ListToolsAsync(cancellationToken: cancellationToken);
        return tools.Select(t => t.ProtocolTool.Name).ToList();
    }

    /// <summary>Calls a tool, records it, and returns the text the Agent would read.</summary>
    public async Task<CallRecord> CallAsync(string tool, object? arguments = null, bool record = true, CancellationToken cancellationToken = default)
    {
        var args = ToDictionary(arguments);
        var stopwatch = Stopwatch.StartNew();
        var result = await _client.CallToolAsync(tool, args, cancellationToken: cancellationToken);
        stopwatch.Stop();

        var (text, tokens) = Flatten(result);
        var call = new CallRecord(tool, tokens, stopwatch.Elapsed.TotalMilliseconds, result.IsError == true || LooksLikeError(text), text);
        if (record)
        {
            _calls.Add(call);
        }

        return call;
    }

    public void ResetCalls() => _calls.Clear();

    private static (string Text, int Tokens) Flatten(CallToolResult result)
    {
        var texts = new List<string>();
        var tokens = 0;

        foreach (var block in result.Content)
        {
            switch (block)
            {
                case TextContentBlock textBlock:
                    texts.Add(textBlock.Text);
                    tokens += TokenCounter.Count(textBlock.Text);
                    break;
                case ImageContentBlock imageBlock:
                    var (width, height) = ImageSize.Read(imageBlock.DecodedData.Span);
                    tokens += TokenCounter.CountImage(width, height);
                    texts.Add($"[image {width}x{height}]");
                    break;
                default:
                    var raw = JsonSerializer.Serialize(block);
                    texts.Add(raw);
                    tokens += TokenCounter.Count(raw);
                    break;
            }
        }

        return (string.Join("\n", texts), tokens);
    }

    // v1 reports failures as {"success":false,...} inside a successful MCP result.
    private static bool LooksLikeError(string text) =>
        text.StartsWith("{\"success\":false", StringComparison.Ordinal);

    private static Dictionary<string, object?> ToDictionary(object? arguments)
    {
        if (arguments is null)
        {
            return [];
        }

        if (arguments is Dictionary<string, object?> dictionary)
        {
            return dictionary;
        }

        var element = JsonSerializer.SerializeToElement(arguments);
        return element.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

public sealed record ToolDefinitionStats(int ToolCount, int TotalTokens, IReadOnlyDictionary<string, int> PerTool);
