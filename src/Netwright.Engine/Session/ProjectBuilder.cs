using System.ComponentModel;
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
    // MSBuild reads Directory.Build.rsp from the project folder and its parents on its own, and a
    // response file can add any switch (targets, loggers, properties). The evaluation runs before the
    // allow list check, so neither run takes switches from the project's folders.
    private const string NoAutoResponse = "-noAutoResponse";

    public static async Task<(string FileName, string Arguments)> BuildAsync(
        string dotnet, string projectPath, string? configuration, string? framework, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullPath))
        {
            throw new NetwrightException(ErrorCodes.LaunchFailed, $"Project not found: {fullPath}", "Pass the path of a .csproj, .vbproj or .fsproj file.");
        }

        List<string> arguments = ["msbuild", fullPath, "-restore", "-t:Build", "-nologo", "-v:m", NoAutoResponse];
        AppendProperties(arguments, configuration, framework);

        // Build servers and reusable MSBuild nodes outlive the build and inherit our pipes, which would
        // keep the output streams open (and this call waiting) for minutes after the build finished.
        arguments.AddRange(["-nodeReuse:false", "-p:UseSharedCompilation=false"]);
        arguments.AddRange(["-getProperty:TargetPath", "-getProperty:RunCommand", "-getProperty:RunArguments"]);

        var result = await RunAsync(dotnet, arguments, fullPath, TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var (exitCode, text) = result
            ?? throw new NetwrightException(ErrorCodes.BuildFailed, $"Building {Path.GetFileName(fullPath)} did not finish within 10 minutes.");

        if (exitCode != 0)
        {
            var errors = ErrorLine().Matches(text).Select(m => m.Value.Trim()).Distinct().Take(15).ToList();
            var detail = errors.Count > 0 ? string.Join(Environment.NewLine, errors) : Tail(text, 15);
            throw new NetwrightException(ErrorCodes.BuildFailed, $"Build failed for {Path.GetFileName(fullPath)}:{Environment.NewLine}{detail}", "Fix the errors and launch again.");
        }

        return ResolveExecutable(text, fullPath);
    }

    /// <summary>
    /// The executable a project is expected to produce, for the allow list check before the build. The
    /// name is the project's evaluated <c>AssemblyName</c> (evaluation only: props are imported, no targets run), so
    /// a name set in Directory.Build.props or built from properties counts too. When the evaluation
    /// fails it falls back to the project file name (<c>Evaluated</c> is false); the build then reports
    /// the failure in detail.
    /// </summary>
    public static async Task<(string Path, bool Evaluated)> ExpectedExecutableAsync(
        string dotnet, string projectPath, string? configuration, string? framework, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var fallback = (Path.ChangeExtension(fullPath, ".exe"), false);
        if (!File.Exists(fullPath))
        {
            return fallback;
        }

        List<string> arguments = ["msbuild", fullPath, "-nologo", "-nodeReuse:false", NoAutoResponse];
        AppendProperties(arguments, configuration, framework);
        arguments.Add("-getProperty:AssemblyName");

        try
        {
            var result = await RunAsync(dotnet, arguments, fullPath, TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return result is (0, var output) && AssemblyNameFrom(output) is { } name
                ? (Path.Combine(Path.GetDirectoryName(fullPath)!, name + ".exe"), true)
                : fallback;
        }
        catch (Exception ex) when (ex is Win32Exception or NetwrightException)
        {
            return fallback;
        }
    }

    /// <summary>The value printed by <c>msbuild -getProperty:AssemblyName</c>; null when it is no usable file name.</summary>
    internal static string? AssemblyNameFrom(string output)
    {
        var name = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
        return string.IsNullOrEmpty(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? null : name;
    }

    /// <summary>
    /// Adds Configuration and TargetFramework. Both come from the Agent and reach a run that precedes the
    /// allow list check, so anything but a plain name is refused: a space, <c>;</c>, a leading <c>-</c>
    /// or an <c>@</c> could otherwise add MSBuild switches, properties or response files.
    /// </summary>
    private static void AppendProperties(List<string> arguments, string? configuration, string? framework)
    {
        arguments.Add("-p:Configuration=" + (string.IsNullOrWhiteSpace(configuration) ? "Debug" : PlainName("configuration", configuration, "Debug or Release")));
        if (!string.IsNullOrWhiteSpace(framework))
        {
            arguments.Add("-p:TargetFramework=" + PlainName("framework", framework, "net8.0-windows"));
        }
    }

    private static string PlainName(string parameter, string value, string example) =>
        PlainNameValue().IsMatch(value)
            ? value
            : throw NetwrightException.InvalidArgument(
                $"Invalid {parameter} '{value}'.",
                $"Pass a plain name of letters, digits, '.', '_' and '-' that starts with a letter or digit, e.g. {example}.");

    /// <summary>
    /// Runs the .NET SDK host in the project's folder; null when it did not finish in time or the caller
    /// cancelled, so callers check their token first.
    /// </summary>
    private static async Task<(int ExitCode, string Output)?> RunAsync(
        string dotnet, IEnumerable<string> arguments, string projectPath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(dotnet, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
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

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return null;
        }

        // Give the readers a moment to drain; never wait for grandchildren that still hold the pipes.
        await Task.WhenAny(pump, Task.Delay(2000, CancellationToken.None)).ConfigureAwait(false);
        lock (output)
        {
            return (process.ExitCode, output.ToString());
        }
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

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\z")]
    private static partial Regex PlainNameValue();
}
