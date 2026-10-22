using Netwright.Engine;
using Netwright.Engine.Session;

namespace Netwright.IntegrationTests;

public sealed partial class SessionTests
{
    [Fact]
    public async Task Launch_sets_environment_variables_for_the_target_app()
    {
        Fixtures.KillStrays();
        await using var session = new DesktopSession();
        await session.LaunchAsync(new LaunchRequest
        {
            Path = Fixtures.ExecutablePath(FixtureKind.Wpf),
            Arguments = ["--no-activate"],
            EnvironmentVariables = new Dictionary<string, string> { ["NETWRIGHT_FIXTURE_ENV"] = "from launch" },
        });

        await session.ExpectAsync("#lblEnv", new ExpectRequest { Assertion = Assertion.Text, Expected = "Env: from launch" });
        Assert.Contains("NETWRIGHT_FIXTURE_ENV=from launch", session.Recorder.Steps[0].Argument("environment"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("netwright_hidden")]
    [InlineData("A=B")]
    [InlineData(" ")]
    public async Task Launch_refuses_netwright_hidden_and_malformed_names(string name)
    {
        await using var session = new DesktopSession();

        var error = await Assert.ThrowsAsync<NetwrightException>(() => session.LaunchAsync(new LaunchRequest
        {
            Path = Fixtures.ExecutablePath(FixtureKind.Wpf),
            EnvironmentVariables = new Dictionary<string, string> { [name] = "x" },
        }));

        Assert.Equal(ErrorCodes.InvalidArgument, error.Code);
    }
}
