using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Netwright.Engine.Session;

/// <summary>
/// Builds a project with the .NET SDK and finds the executable of the actual app, so the Target App
/// PID is the app itself rather than a <c>dotnet run</c> host.
/// </summary>
internal static partial class ProjectBuilder
{
    public static async Task<(string FileName, string Arguments)> BuildAsync(
        string dotnet, string projectPath, string? configuration, string? framework, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullPath))
        {
            throw new NetwrightException(ErrorCodes.LaunchFailed, $"Project not found: {fullPath}", "Pass the path of a .csproj, .vbproj or .fsproj file.");
        }

        var arguments = new StringBuilder()
            .Append("msbuild \"").Append(fullPath).Append("\" -restore -t:Build -nologo -v:m")
            .Append(" -p:Configuration=").Append(string.IsNullOrWhiteSpace(configuration) ? "Debug" : configuration);
        if (!string.IsNullOrWhiteSpace(framework))
        {
            arguments.Append(" -p:TargetFramework=").Append(framework);
        }

        // Build servers and reusable MSBuild nodes outlive the build and inherit our pipes, which would
        // keep the output streams open (and this call waiting) for minutes after the build finished.
        arguments.Append(" -nodeReuse:false -p:UseSharedCompilation=false");
        arguments.Append(" -getProperty:TargetPath -getProperty:RunCommand -getProperty:RunArguments");

        var startInfo = new ProcessStartInfo(dotnet, arguments.ToString())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(fullPath)!,
            StandardOutputEncoding = Encoding.UTF8,
        };
        startInfo.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        startInfo.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(startInfo) ?? throw new NetwrightException(ErrorCodes.BuildFailed, $"Could not start '{dotnet}'.", "Install the .NET SDK or set NETWRIGHT_DOTNET.");
        var output = new StringBuilder();
        var pump = Task.WhenAll(Pump(process.StandardOutput, output), Pump(process.StandardError, output));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new NetwrightException(ErrorCodes.BuildFailed, $"Building {Path.GetFileName(fullPath)} did not finish within 10 minutes.");
        }

        // Give the readers a moment to drain; never wait for grandchildren that still hold the pipes.
        await Task.WhenAny(pump, Task.Delay(2000, CancellationToken.None)).ConfigureAwait(false);
        string text;
        lock (output)
        {
            text = output.ToString();
        }

        if (process.ExitCode != 0)
        {
            var errors = ErrorLine().Matches(text).Select(m => m.Value.Trim()).Distinct().Take(15).ToList();
            var detail = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : Tail(text, 15);
            throw new NetwrightException(ErrorCodes.BuildFailed, $"Build failed for {Path.GetFileName(fullPath)}:{Environment.NewLine}{detail}", "Fix the errors and launch again.");
        }

        return ResolveExecutable(text, fullPath);
    }

    private static async Task Pump(StreamReader reader, StringBuilder sink)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            lock (sink)
            {
                sink.AppendLine(line);
            }
        }
    }

    internal static (string FileName, string Arguments) ResolveExecutable(string output, string projectPath)
    {
        var jsonStart = output.IndexOf('{', StringComparison.Ordinal);
        if (jsonStart < 0)
        {
            throw new NetwrightException(ErrorCodes.BuildFailed, $"Could not read the build output path of {Path.GetFileName(projectPath)}.", Tail(output, 10));
        }

        using var document = JsonDocument.Parse(output[jsonStart..(output.LastIndexOf('}') + 1)]);
        var properties = document.RootElement.GetProperty("Properties");
        var targetPath = properties.TryGetProperty("TargetPath", out var t) ? t.GetString() ?? "" : "";
        var runCommand = properties.TryGetProperty("RunCommand", out var c) ? c.GetString() ?? "" : "";
        var runArguments = properties.TryGetProperty("RunArguments", out var a) ? a.GetString() ?? "" : "";

        if (runCommand.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !IsDotnetHost(runCommand))
        {
            return (runCommand, runArguments);
        }

        if (targetPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return (targetPath, "");
        }

        if (IsDotnetHost(runCommand))
        {
            return (runCommand, runArguments);
        }

        throw new NetwrightException(
            ErrorCodes.LaunchFailed,
            $"{Path.GetFileName(projectPath)} does not produce a runnable app (TargetPath: {targetPath}).",
            "If the project targets several frameworks, pass framework, e.g. net8.0-windows.");
    }

    private static bool IsDotnetHost(string command) =>
        Path.GetFileNameWithoutExtension(command).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    private static string Tail(string text, int lines) =>
        string.Join(Environment.NewLine, text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).TakeLast(lines));

    [GeneratedRegex(@"[^\r\n]*: error [A-Z]+\d+:[^\r\n]*")]
    private static partial Regex ErrorLine();
}
