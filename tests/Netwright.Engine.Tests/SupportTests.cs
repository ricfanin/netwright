using Netwright.Engine.Diagnostics;
using Netwright.Engine.Input;
using Netwright.Engine.Refs;
using Netwright.Engine.Session;
using Netwright.Engine.Uia;
using static Netwright.Engine.Tests.TestTree;

namespace Netwright.Engine.Tests;

public class KeyParserTests
{
    [Fact]
    public void Parses_chords_and_sequences()
    {
        var chords = KeyParser.Parse("Ctrl+Shift+S Tab F5 a 7");
        Assert.Equal([VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_S], chords[0]);
        Assert.Equal([VirtualKeyShort.TAB], chords[1]);
        Assert.Equal([VirtualKeyShort.F5], chords[2]);
        Assert.Equal([VirtualKeyShort.KEY_A], chords[3]);
        Assert.Equal([VirtualKeyShort.KEY_7], chords[4]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Banana")]
    [InlineData("F25")]
    public void Rejects_unknown_keys(string keys)
    {
        var ex = Assert.Throws<NetwrightException>(() => KeyParser.Parse(keys));
        Assert.Equal(ErrorCodes.InvalidArgument, ex.Code);
    }
}

public class RefRegistryTests
{
    [Fact]
    public void Same_element_keeps_its_ref()
    {
        var refs = new RefRegistry();
        var first = refs.GetOrAssign(Button("Save", key: "1.2.3"));
        var other = refs.GetOrAssign(Button("Cancel", key: "1.2.4"));
        var again = refs.GetOrAssign(Button("Save (renamed)", key: "1.2.3"));

        Assert.Equal("e1", first);
        Assert.Equal("e2", other);
        Assert.Equal(first, again);
    }

    [Fact]
    public void Resolving_a_removed_element_reports_a_stale_ref()
    {
        var refs = new RefRegistry();
        var id = refs.GetOrAssign(Button("Save", key: "gone"));

        var ex = Assert.Throws<NetwrightException>(() => refs.Resolve(id, Tree(Window("Main"))));

        Assert.Equal(ErrorCodes.StaleRef, ex.Code);
        Assert.Contains("button \"Save\"", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Recycled_virtualized_item_is_treated_as_stale()
    {
        var refs = new RefRegistry();
        var id = refs.GetOrAssign(Node(ControlTypeIds.ListItem, "Order 1", key: "container"));
        var tree = Tree(Window("Main", Node(ControlTypeIds.ListItem, "Order 57", key: "container")));

        Assert.Equal(ErrorCodes.StaleRef, Assert.Throws<NetwrightException>(() => refs.Resolve(id, tree)).Code);
    }

    [Fact]
    public void Unknown_ref_is_not_found()
    {
        var ex = Assert.Throws<NetwrightException>(() => new RefRegistry().Resolve("e99", Tree()));
        Assert.Equal(ErrorCodes.ElementNotFound, ex.Code);
    }

    [Theory]
    [InlineData("e1", true)]
    [InlineData("e123", true)]
    [InlineData("e", false)]
    [InlineData("#e1", false)]
    [InlineData("e1a", false)]
    public void Recognizes_ref_shape(string text, bool expected) => Assert.Equal(expected, RefRegistry.LooksLikeRef(text));
}

public class ProjectBuilderTests
{
    [Fact]
    public void Prefers_apphost_run_command()
    {
        const string output = """
            {
              "Properties": {
                "TargetPath": "C:\\app\\bin\\Debug\\net8.0-windows\\App.dll",
                "RunCommand": "C:\\app\\bin\\Debug\\net8.0-windows\\App.exe",
                "RunArguments": ""
              }
            }
            """;

        var (file, args) = ProjectBuilder.ResolveExecutable(output, "App.csproj");

        Assert.EndsWith("App.exe", file, StringComparison.Ordinal);
        Assert.Equal("", args);
    }

    [Fact]
    public void Uses_target_path_for_net_framework_apps()
    {
        const string output = """
            build noise
            {"Properties":{"TargetPath":"C:\\legacy\\bin\\Legacy.exe","RunCommand":"","RunArguments":""}}
            """;

        Assert.Equal("C:\\legacy\\bin\\Legacy.exe", ProjectBuilder.ResolveExecutable(output, "Legacy.csproj").FileName);
    }

    [Fact]
    public void Falls_back_to_dotnet_host()
    {
        const string output = """{"Properties":{"TargetPath":"C:\\a\\App.dll","RunCommand":"C:\\Program Files\\dotnet\\dotnet.exe","RunArguments":"exec \"C:\\a\\App.dll\""}}""";

        var (file, args) = ProjectBuilder.ResolveExecutable(output, "App.csproj");

        Assert.EndsWith("dotnet.exe", file, StringComparison.Ordinal);
        Assert.Contains("App.dll", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Library_projects_are_rejected()
    {
        const string output = """{"Properties":{"TargetPath":"C:\\a\\Lib.dll","RunCommand":"","RunArguments":""}}""";

        Assert.Equal(ErrorCodes.LaunchFailed, Assert.Throws<NetwrightException>(() => ProjectBuilder.ResolveExecutable(output, "Lib.csproj")).Code);
    }
}

public class CrashReporterTests
{
    [Fact]
    public void Extracts_exception_from_runtime_event()
    {
        const string eventText = "Application: Netwright.Fixtures.Wpf.exe\r\nCoreCLR Version: 8.0.2024.46610\r\n.NET Version: 8.0.20\r\nDescription: The process was terminated due to an unhandled exception.\r\nException Info: System.InvalidOperationException: Fixture crash requested\r\n   at Netwright.Fixtures.Wpf.MainWindow.OnCrash(Object sender, RoutedEventArgs e)\r\n   at System.Windows.EventRoute.InvokeHandlersImpl(Object source, RoutedEventArgs args, Boolean reRaised)\r\n";

        var lines = CrashReporter.ExtractFromEvent(eventText);

        Assert.Equal("System.InvalidOperationException: Fixture crash requested", lines[0]);
        Assert.StartsWith("   at Netwright.Fixtures.Wpf.MainWindow.OnCrash", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Extracts_exception_from_stderr()
    {
        var stderr = new[] { "some log", "Unhandled exception. System.InvalidOperationException: Fixture crash requested", "   at App.Main()" };

        var lines = CrashReporter.ExtractFromStderr(stderr);

        Assert.Equal("System.InvalidOperationException: Fixture crash requested", lines[0]);
        Assert.Equal(2, lines.Count);
    }
}

public class AppOutputTests
{
    [Fact]
    public void Returns_only_new_lines_by_default()
    {
        var output = new AppOutput();
        output.Add(OutputStreams.Stdout, "one");
        output.Add(OutputStreams.Debug, "two");

        Assert.Equal(2, output.Read(null, 10, includeAlreadyRead: false).Entries.Count);
        output.Add(OutputStreams.Stderr, "three");

        var (entries, _) = output.Read(null, 10, includeAlreadyRead: false);
        Assert.Equal("three", Assert.Single(entries).Text);
        Assert.Equal(3, output.Read(null, 10, includeAlreadyRead: true).Entries.Count);
    }

    [Fact]
    public void Filters_by_stream_and_keeps_the_newest_when_limited()
    {
        var output = new AppOutput();
        for (var i = 0; i < 5; i++)
        {
            output.Add(OutputStreams.Stdout, $"line {i}");
        }

        output.Add(OutputStreams.Stderr, "error");

        var (entries, skipped) = output.Read(OutputStreams.Stdout, 2, includeAlreadyRead: true);
        Assert.Equal(["line 3", "line 4"], entries.Select(e => e.Text));
        Assert.Equal(3, skipped);
    }
}

public class ArgumentQuotingTests
{
    [Fact]
    public void Quotes_arguments_with_spaces_and_quotes()
    {
        Assert.Equal("--tab Grid \"C:\\My Files\\a.txt\" \"say \\\"hi\\\"\" \"\"", DesktopSession.JoinArguments(["--tab", "Grid", "C:\\My Files\\a.txt", "say \"hi\"", ""]));
    }
}
