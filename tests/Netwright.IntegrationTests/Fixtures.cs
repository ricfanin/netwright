using System.Diagnostics;
using Netwright.Engine.Session;

namespace Netwright.IntegrationTests;

public enum FixtureKind
{
    Wpf,
    WinForms,
    WinFormsNetFramework,
    WinUI,
}

/// <summary>Locates the built Fixture Apps and starts sessions against them.</summary>
public static class Fixtures
{
    public static string RepositoryRoot { get; } = FindRoot();

    public static string ExecutablePath(FixtureKind kind)
    {
        var (project, framework) = kind switch
        {
            FixtureKind.Wpf => ("Netwright.Fixtures.Wpf", "net8.0-windows"),
            FixtureKind.WinForms => ("Netwright.Fixtures.WinForms", "net8.0-windows"),
            FixtureKind.WinUI => ("Netwright.Fixtures.WinUI", Path.Combine("net8.0-windows10.0.19041.0", "win-x64")),
            _ => ("Netwright.Fixtures.WinForms", "net48"),
        };

        foreach (var configuration in new[] { "Release", "Debug" })
        {
            var path = Path.Combine(RepositoryRoot, "tests", "fixtures", project, "bin", configuration, framework, project + ".exe");
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new InvalidOperationException($"Fixture {project} ({framework}) is not built. Run: dotnet build tests/fixtures/{project} -c Release");
    }

    public static async Task<DesktopSession> LaunchAsync(FixtureKind kind, string tab = "Form", SessionOptions? options = null)
    {
        KillStrays();
        var session = new DesktopSession(options ?? new SessionOptions());
        await session.LaunchAsync(new LaunchRequest { Path = ExecutablePath(kind), Arguments = ["--tab", tab, "--no-activate"] });
        return session;
    }

    public static void KillStrays()
    {
        foreach (var name in new[] { "Netwright.Fixtures.Wpf", "Netwright.Fixtures.WinForms", "Netwright.Fixtures.WinUI" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "tests", "fixtures")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}

/// <summary>UI tests drive real windows and must never run in parallel.</summary>
[CollectionDefinition("UI", DisableParallelization = true)]
public sealed class UiCollection;
