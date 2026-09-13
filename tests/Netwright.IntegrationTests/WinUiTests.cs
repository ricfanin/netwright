using Netwright.Engine.Session;

namespace Netwright.IntegrationTests;

/// <summary>
/// Tier 2 coverage: the core flows against the WinUI 3 Fixture App (which also stands in for .NET MAUI
/// on Windows). Build it first: dotnet build tests/fixtures/Netwright.Fixtures.WinUI -c Release
/// </summary>
[Collection("UI")]
[Trait("Tier", "2")]
public sealed class WinUiTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Snapshot_shows_the_form()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.WinUI);

        var snapshot = (await session.SnapshotAsync()).Text;
        output.WriteLine(snapshot);

        Assert.Matches(@"textbox ""Name"" \[e\d+\] #txtName", snapshot);
        Assert.Matches(@"button ""Submit"" \[e\d+\] #btnSubmit disabled", snapshot);
    }

    [Fact]
    public async Task Form_submit_flow()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.WinUI);

        await session.TypeAsync("#txtName", new TypeRequest { Text = "Ada Lovelace" });
        await session.TypeAsync("#txtEmail", new TypeRequest { Text = "ada@example.com" });
        await session.SetStateAsync("#cmbCountry", new SetStateRequest { Item = "Italy" });
        await session.SetStateAsync("#chkTerms", new SetStateRequest { Checked = true });
        var clicked = await session.ClickAsync("#btnSubmit");
        output.WriteLine(clicked.Changes.Text);

        await session.ExpectAsync("#lblResult", new ExpectRequest { Assertion = Assertion.TextContains, Expected = "Submitted: Ada Lovelace <ada@example.com> Italy" });
    }

    [Fact]
    public async Task Async_load_and_wait_for_text()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.WinUI, "Async");

        await session.ClickAsync("#btnLoad");
        var waited = await session.WaitAsync(new WaitRequest { Text = "Loaded 3 items", TimeoutMs = 5000 });
        output.WriteLine(waited.Changes.Text);

        await session.ExpectAsync("#lstItems >> item", new ExpectRequest { Assertion = Assertion.Count, Expected = "3" });
    }

    [Fact]
    public async Task Modal_window_can_be_answered()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.WinUI);

        var opened = await session.ClickAsync("#btnOpenModal");
        output.WriteLine(opened.Changes.Text);
        await session.ClickAsync("window \"Confirm\" >> button \"Yes\"");

        await session.ExpectAsync("#lblModalResult", new ExpectRequest { Assertion = Assertion.Text, Expected = "Modal result: Yes" });
    }

    [Fact]
    public async Task Offscreen_item_is_scrolled_into_view_and_clicked()
    {
        await using var session = await Fixtures.LaunchAsync(FixtureKind.WinUI, "Scroll");

        await session.ClickAsync("#btnItem100");

        await session.ExpectAsync("#lblScrollClicked", new ExpectRequest { Assertion = Assertion.Text, Expected = "Clicked Item 100" });
    }
}
