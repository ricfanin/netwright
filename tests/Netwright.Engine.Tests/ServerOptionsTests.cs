namespace Netwright.Engine.Tests;

public sealed class ServerOptionsTests
{
    private static readonly Func<string, string?> NoEnvironment = _ => null;

    private static Func<string, string?> ScreenshotDir(string value) => name => name == "NETWRIGHT_SCREENSHOT_DIR" ? value : null;

    [Fact]
    public void Saving_screenshots_is_off_by_default()
    {
        Assert.Null(ServerOptions.Parse([], NoEnvironment).Session.ScreenshotDirectory);
    }

    [Fact]
    public void Reads_the_screenshot_directory_from_the_flag()
    {
        var directory = Path.Combine(Path.GetTempPath(), "shots");

        Assert.Equal(directory, ServerOptions.Parse(["--screenshot-dir", directory], NoEnvironment).Session.ScreenshotDirectory);
    }

    [Fact]
    public void Reads_the_screenshot_directory_from_the_environment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "shots");

        var options = ServerOptions.Parse([], ScreenshotDir(directory));

        Assert.Equal(directory, options.Session.ScreenshotDirectory);
    }

    [Fact]
    public void The_flag_wins_over_the_environment()
    {
        var directory = Path.Combine(Path.GetTempPath(), "from-flag");

        var options = ServerOptions.Parse(["--screenshot-dir", directory], ScreenshotDir(Path.Combine(Path.GetTempPath(), "from-environment")));

        Assert.Equal(directory, options.Session.ScreenshotDirectory);
    }

    [Fact]
    public void Resolves_a_relative_directory_against_the_working_directory_at_startup()
    {
        var expected = Path.Combine(Environment.CurrentDirectory, "reports", "shots");

        Assert.Equal(expected, ServerOptions.Parse(["--screenshot-dir", "reports/shots"], NoEnvironment).Session.ScreenshotDirectory);
        Assert.Equal(expected, ServerOptions.Parse([], ScreenshotDir("reports/shots")).Session.ScreenshotDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("shots\0")]
    public void Refuses_an_invalid_directory_with_a_clear_error(string value)
    {
        var error = Assert.Throws<ArgumentException>(() => ServerOptions.Parse(["--screenshot-dir", value], NoEnvironment));

        Assert.StartsWith("--screenshot-dir needs a directory path", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_an_invalid_directory_from_the_environment_with_a_clear_error()
    {
        var error = Assert.Throws<ArgumentException>(() => ServerOptions.Parse([], ScreenshotDir("shots\0")));

        Assert.StartsWith("NETWRIGHT_SCREENSHOT_DIR needs a directory path", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_flag_needs_a_value()
    {
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(["--screenshot-dir"], NoEnvironment));
    }
}
