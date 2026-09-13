using System.Diagnostics;
using Interop.UIAutomationClient;
using Netwright.Engine.Uia;

namespace Netwright.IntegrationTests;

/// <summary>
/// Measures capture cost so changes to caching are driven by numbers. Run with:
/// dotnet test --filter Category=Perf --logger "console;verbosity=detailed"
/// </summary>
[Collection("UI")]
[Trait("Category", "Perf")]
public sealed class CapturePerformanceProbe(ITestOutputHelper output)
{
    [Theory]
    [InlineData(FixtureKind.Wpf, "Form")]
    [InlineData(FixtureKind.Wpf, "Grid")]
    [InlineData(FixtureKind.WinForms, "Form")]
    [InlineData(FixtureKind.WinForms, "Grid")]
    public async Task Capture_cost(FixtureKind kind, string tab)
    {
        await using var session = await Fixtures.LaunchAsync(kind, tab);
        await Task.Delay(500);
        var reader = session.Reader;
        var pids = session.CurrentApp!.ProcessIds;
        var automation = reader.Automation;

        var onscreenOnly = reader.CreateRequest(
            PropertyIds.FullSet.Select(p => p.Id),
            AutomationElementMode.AutomationElementMode_Full,
            automation.CreateAndCondition(automation.ControlViewCondition, automation.CreatePropertyCondition(PropertyIds.IsOffscreen, false)));

        Measure("window enumeration", () => UiaTreeReader.TopLevelWindowHandles(pids).Count);
        Measure("full", () => reader.Capture(pids).AllNodes.Count());
        Measure("light", () => reader.Capture(pids, CaptureDetail.Light).AllNodes.Count());
        Measure("full, onscreen only", () => reader.CaptureWith(pids, onscreenOnly, CaptureDetail.Full).AllNodes.Count());

        void Measure(string label, Func<int> capture)
        {
            var timings = new List<double>();
            var count = 0;
            var iterations = kind == FixtureKind.WinForms && tab == "Grid" ? 2 : 8;
            for (var i = 0; i < iterations; i++)
            {
                var stopwatch = Stopwatch.StartNew();
                count = capture();
                timings.Add(stopwatch.Elapsed.TotalMilliseconds);
            }

            output.WriteLine($"{kind}/{tab} {label}: {count} items, median {timings.Order().ElementAt(timings.Count / 2):F1} ms, min {timings.Min():F1} ms");
        }
    }
}
