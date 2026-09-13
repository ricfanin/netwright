namespace Netwright.Benchmarks;

/// <summary>How to start an MCP server over stdio.</summary>
public sealed record ServerDefinition(
    string Name,
    string Command,
    IList<string> Arguments,
    string? WorkingDirectory = null,
    Dictionary<string, string?>? Environment = null,
    string? ProtocolVersion = null);
