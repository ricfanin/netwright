using System.Globalization;
using System.Text;

namespace Netwright.Benchmarks;

/// <summary>Renders several runs side by side as Markdown tables.</summary>
public static class MarkdownReport
{
    public static string Render(IReadOnlyList<BenchmarkRun> runs)
    {
        var sb = new StringBuilder();
        var first = runs[0];

        sb.AppendLine("# Netwright Benchmark");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Machine: `{first.Machine}` — {first.OperatingSystem}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Token encoding: `{first.TokenEncoding}` (proxy for Claude's tokenizer; the same encoding is used for every server)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- Iterations per scenario: {first.Iterations}; latency is the sum of tool-call round trips, excluding Agent think time and fixed polling sleeps");
        sb.AppendLine();

        sb.AppendLine("## Tool definitions (sent to the model on every turn)");
        sb.AppendLine();
        sb.AppendLine("| Server | Fixture | Tools | Tokens |");
        sb.AppendLine("|---|---|---:|---:|");
        foreach (var run in runs)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"| {run.Label} | {run.Fixture} | {run.ToolDefinitions.ToolCount} | {run.ToolDefinitions.TotalTokens:N0} |");
        }

        sb.AppendLine();
        var scenarioNames = runs.SelectMany(r => r.Scenarios).Select(s => s.Name).Distinct().ToList();
        foreach (var scenario in scenarioNames)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"## `{scenario}`");
            sb.AppendLine();
            sb.AppendLine("| Server | Fixture | Success | Calls | Tokens (median) | Latency p50 | Latency p95 |");
            sb.AppendLine("|---|---|---:|---:|---:|---:|---:|");
            foreach (var run in runs)
            {
                var result = run.Scenarios.FirstOrDefault(s => s.Name == scenario);
                if (result is null)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"| {run.Label} | {run.Fixture} | not measured¹ | | | | |");
                    continue;
                }

                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"| {run.Label} | {run.Fixture} | {result.Successes}/{result.Iterations} | {result.MedianCalls:0.#} | {result.MedianTokens:N0} | {result.LatencyP50Ms:N0} ms | {result.LatencyP95Ms:N0} ms |");
            }

            sb.AppendLine();
        }

        if (runs.Any(r => r.Scenarios.Count < scenarioNames.Count))
        {
            sb.AppendLine("¹ The server's tools act at screen coordinates with the real mouse and keyboard, so scripted multi-step scenarios were not run against it.");
            sb.AppendLine();
            sb.AppendLine("Windows-MCP snapshots describe the whole desktop (interactive elements of visible windows with coordinates) rather than the app's tree, so their token counts are not directly comparable: the grid snapshot, for example, does not list the grid's rows.");
        }

        return sb.ToString();
    }
}
