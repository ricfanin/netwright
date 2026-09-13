using System.Runtime.InteropServices;
using System.Text.Json;
using Netwright.Benchmarks;
using Netwright.Benchmarks.Adapters;

// Usage:
//   run    --adapter v1|v2 --command <exe> [--arg <value>]... --fixture <exe> --fixture-name <wpf|winforms>
//          --label <text> [--iterations 5] [--scenarios a,b] --out <file.json>
//   report --out <file.md> <run.json>...

var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

if (args.Length == 0)
{
    Console.Error.WriteLine("Commands: run, report. See Program.cs for options.");
    return 2;
}

switch (args[0])
{
    case "run":
        return await RunAsync(args[1..]);
    case "report":
        return Report(args[1..]);
    default:
        Console.Error.WriteLine($"Unknown command '{args[0]}'.");
        return 2;
}

async Task<int> RunAsync(string[] options)
{
    var parsed = Options.Parse(options);
    var adapterName = parsed.Required("adapter");
    IServerAdapter adapter = adapterName switch
    {
        "v1" => new V1Adapter(),
        _ => throw new ArgumentException($"Unknown adapter '{adapterName}'."),
    };

    var fixture = Path.GetFullPath(parsed.Required("fixture"));
    if (!File.Exists(fixture))
    {
        Console.Error.WriteLine($"Fixture not found: {fixture}");
        return 1;
    }

    var server = new ServerDefinition(parsed.Required("label"), parsed.Required("command"), parsed.All("arg"));
    var iterations = int.Parse(parsed.Optional("iterations") ?? "5", System.Globalization.CultureInfo.InvariantCulture);
    var only = parsed.Optional("scenarios")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    Console.WriteLine($"Benchmarking {server.Name} ({adapter.Name}) against {fixture}");
    await using var session = await McpSession.StartAsync(server);
    var toolDefinitions = await session.MeasureToolDefinitionsAsync();
    Console.WriteLine($"  tool definitions: {toolDefinitions.ToolCount} tools, {toolDefinitions.TotalTokens} tokens");

    await adapter.InitializeAsync(session);
    var runner = new ScenarioRunner(adapter, session, fixture, iterations);
    var scenarios = await runner.RunAsync(only);

    var run = new BenchmarkRun(
        server.Name,
        adapter.Name,
        parsed.Required("fixture-name"),
        DateTimeOffset.Now,
        MachineDescription(),
        RuntimeInformation.OSDescription,
        TokenCounter.EncodingName,
        iterations,
        toolDefinitions,
        scenarios);

    var outPath = Path.GetFullPath(parsed.Required("out"));
    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
    await File.WriteAllTextAsync(outPath, JsonSerializer.Serialize(run, jsonOptions));
    Console.WriteLine($"Wrote {outPath}");
    return scenarios.All(s => s.Successes == s.Iterations) ? 0 : 1;
}

int Report(string[] options)
{
    var parsed = Options.Parse(options);
    var runs = parsed.Positional
        .Select(path => JsonSerializer.Deserialize<BenchmarkRun>(File.ReadAllText(path), jsonOptions)!)
        .ToList();
    if (runs.Count == 0)
    {
        Console.Error.WriteLine("No run files given.");
        return 2;
    }

    var outPath = parsed.Required("out");
    File.WriteAllText(outPath, MarkdownReport.Render(runs));
    Console.WriteLine($"Wrote {outPath}");
    return 0;
}

static string MachineDescription()
{
    var cpu = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "unknown CPU";
    return $"{cpu}, {Environment.ProcessorCount} logical cores";
}

internal sealed class Options
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Positional { get; } = [];

    public static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
            {
                var key = args[i][2..];
                if (!options._values.TryGetValue(key, out var list))
                {
                    options._values[key] = list = [];
                }

                list.Add(args[++i]);
            }
            else
            {
                options.Positional.Add(args[i]);
            }
        }

        return options;
    }

    public string Required(string key) =>
        Optional(key) ?? throw new ArgumentException($"Missing required option --{key}.");

    public string? Optional(string key) =>
        _values.TryGetValue(key, out var list) ? list[^1] : null;

    public List<string> All(string key) =>
        _values.TryGetValue(key, out var list) ? list : [];
}
