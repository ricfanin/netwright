namespace Netwright.Benchmarks;

public sealed record BenchmarkRun(
    string Label,
    string Adapter,
    string Fixture,
    DateTimeOffset Timestamp,
    string Machine,
    string OperatingSystem,
    string TokenEncoding,
    int Iterations,
    ToolDefinitionStats ToolDefinitions,
    IReadOnlyList<ScenarioResult> Scenarios);

public sealed record ScenarioResult(
    string Name,
    int Iterations,
    int Successes,
    double MedianTokens,
    double MedianCalls,
    double LatencyP50Ms,
    double LatencyP95Ms,
    IReadOnlyList<CallSample> SampleTranscript);

/// <summary>A recorded call from the first iteration, kept so results can be inspected.</summary>
public sealed record CallSample(string Tool, int Tokens, double LatencyMs, bool IsError, string TextPreview);

public static class Stats
{
    public static double Median(IEnumerable<double> values) => Percentile(values, 50);

    public static double Percentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0)
        {
            return 0;
        }

        // Nearest-rank method: simple and unambiguous for small samples.
        var rank = (int)Math.Ceiling(percentile / 100.0 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}
