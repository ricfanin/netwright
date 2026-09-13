using System.Diagnostics;
using Netwright.Benchmarks.Adapters;

namespace Netwright.Benchmarks;

public sealed class ScenarioRunner(IServerAdapter adapter, McpSession session, string fixturePath, int iterations)
{
    private const int PreviewLength = 2000;

    private static readonly (string Name, string Tab, Func<IServerAdapter, McpSession, Task<bool>> Run)[] AllScenarios =
    [
        ("snapshot-form", "Form", (a, s) => a.SnapshotAsync(s)),
        ("snapshot-grid", "Grid", (a, s) => a.SnapshotAsync(s)),
        ("form-submit", "Form", (a, s) => a.FormSubmitAsync(s)),
        ("async-load", "Async", (a, s) => a.AsyncLoadAsync(s)),
        ("screenshot", "Form", (a, s) => a.ScreenshotAsync(s)),
    ];

    public static IReadOnlyList<string> ScenarioNames => AllScenarios.Select(s => s.Name).ToList();

    public async Task<IReadOnlyList<ScenarioResult>> RunAsync(IReadOnlyCollection<string>? only)
    {
        var results = new List<ScenarioResult>();

        foreach (var scenario in AllScenarios.Where(s => (only is null || only.Contains(s.Name)) && adapter.Supports(s.Name)))
        {
            Console.WriteLine($"  scenario {scenario.Name}");
            var tokens = new List<double>();
            var calls = new List<double>();
            var latencies = new List<double>();
            var successes = 0;
            IReadOnlyList<CallSample> sample = [];

            for (var i = 0; i < iterations; i++)
            {
                KillFixtureProcesses();
                await adapter.LaunchFixtureAsync(session, fixturePath, scenario.Tab);
                await Task.Delay(500); // let the window finish its first layout

                session.ResetCalls();
                bool success;
                try
                {
                    success = await scenario.Run(adapter, session);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    iteration {i + 1} threw: {ex.Message}");
                    success = false;
                }

                var recorded = session.Calls.ToList();
                if (success)
                {
                    successes++;
                }

                tokens.Add(recorded.Sum(c => c.ResponseTokens));
                calls.Add(recorded.Count);
                latencies.Add(recorded.Sum(c => c.LatencyMs));
                if (i == 0)
                {
                    sample = recorded.Select(c => new CallSample(c.Tool, c.ResponseTokens, Math.Round(c.LatencyMs, 1), c.IsError, adapter.RedactTranscripts ? "[not stored]" : Preview(c.Text))).ToList();
                }

                Console.WriteLine($"    iteration {i + 1}: {(success ? "ok" : "FAILED")}, {recorded.Count} calls, {recorded.Sum(c => c.ResponseTokens)} tokens, {recorded.Sum(c => c.LatencyMs):F0} ms");

                try
                {
                    await adapter.CloseFixtureAsync(session);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"    close failed: {ex.Message}");
                }

                KillFixtureProcesses();
            }

            results.Add(new ScenarioResult(
                scenario.Name,
                iterations,
                successes,
                Stats.Median(tokens),
                Stats.Median(calls),
                Math.Round(Stats.Percentile(latencies, 50), 1),
                Math.Round(Stats.Percentile(latencies, 95), 1),
                sample));
        }

        return results;
    }

    private void KillFixtureProcesses()
    {
        var name = Path.GetFileNameWithoutExtension(fixturePath);
        foreach (var process in Process.GetProcessesByName(name))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static string Preview(string text) =>
        text.Length <= PreviewLength ? text : string.Concat(text.AsSpan(0, PreviewLength), $"... [{text.Length - PreviewLength} more chars]");
}
