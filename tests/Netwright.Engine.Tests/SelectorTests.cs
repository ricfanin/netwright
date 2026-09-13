using Netwright.Engine.Selectors;
using Netwright.Engine.Uia;
using static Netwright.Engine.Tests.TestTree;

namespace Netwright.Engine.Tests;

public class SelectorParserTests
{
    [Fact]
    public void Parses_automation_id()
    {
        var step = Assert.Single(SelectorParser.Parse("#btnSave").Steps);
        Assert.Equal("btnSave", step.AutomationId);
        Assert.Null(step.Role);
        Assert.Null(step.Name);
    }

    [Fact]
    public void Parses_role_and_exact_name()
    {
        var step = Assert.Single(SelectorParser.Parse("button \"Save changes\"").Steps);
        Assert.Equal("button", step.Role);
        Assert.Equal("Save changes", step.Name);
        Assert.Equal(NameMatch.Exact, step.NameMatch);
    }

    [Fact]
    public void Parses_contains_match_and_aliases()
    {
        var step = Assert.Single(SelectorParser.Parse("edit ~\"mail\"").Steps);
        Assert.Equal("textbox", step.Role);
        Assert.Equal(NameMatch.Contains, step.NameMatch);
    }

    [Fact]
    public void Parses_chains_and_nth()
    {
        var selector = SelectorParser.Parse("window \"Confirm\" >> button:nth(2)");
        Assert.Equal(2, selector.Steps.Count);
        Assert.Equal("window", selector.Steps[0].Role);
        Assert.Equal(2, selector.Steps[1].Nth);
    }

    [Fact]
    public void Parses_role_with_id_and_escaped_quotes()
    {
        var step = Assert.Single(SelectorParser.Parse("text#lbl \"say \\\"hi\\\"\"").Steps);
        Assert.Equal("text", step.Role);
        Assert.Equal("lbl", step.AutomationId);
        Assert.Equal("say \"hi\"", step.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("gizmo \"x\"")]
    [InlineData("button \"unterminated")]
    [InlineData("button >>")]
    [InlineData("button:nth(0)")]
    [InlineData("#")]
    [InlineData("~")]
    public void Rejects_invalid_selectors(string text)
    {
        var ex = Assert.Throws<NetwrightException>(() => SelectorParser.Parse(text));
        Assert.Equal(ErrorCodes.InvalidSelector, ex.Code);
        Assert.NotNull(ex.Hint);
    }

    [Theory]
    [InlineData("#btnSave")]
    [InlineData("button \"Save\"")]
    [InlineData("window \"Confirm\" >> button#btnYes")]
    [InlineData("item ~\"Ital\":nth(3)")]
    public void Round_trips_through_ToString(string text)
    {
        var parsed = SelectorParser.Parse(text);
        Assert.Equal(parsed, SelectorParser.Parse(parsed.ToString()), new SelectorComparer());
    }

    private sealed class SelectorComparer : IEqualityComparer<Selector>
    {
        public bool Equals(Selector? x, Selector? y) => x!.Steps.SequenceEqual(y!.Steps);

        public int GetHashCode(Selector obj) => obj.Steps.Count;
    }
}

public class SelectorMatcherTests
{
    private static Model.UiTree SampleTree() => Tree(
        Window("Main",
            Pane(
                Text("Name:"),
                TextBox("Name", "txtName"),
                TextBox("Email", "txtEmail"),
                Button("Submit", "btnSubmit"),
                Button("OK", "ok1"))),
        Window("Confirm",
            Button("OK", "ok2"),
            Button("Cancel")));

    [Fact]
    public void Matches_by_id_role_and_name_case_insensitively()
    {
        var tree = SampleTree();
        Assert.Equal("txtName", Assert.Single(SelectorMatcher.Match(SelectorParser.Parse("#txtName"), tree)).AutomationId);
        Assert.Equal("btnSubmit", Assert.Single(SelectorMatcher.Match(SelectorParser.Parse("button \"submit\""), tree)).AutomationId);
        Assert.Equal(2, SelectorMatcher.Match(SelectorParser.Parse("textbox"), tree).Count);
        Assert.Equal("txtEmail", Assert.Single(SelectorMatcher.Match(SelectorParser.Parse("textbox ~\"mai\""), tree)).AutomationId);
    }

    [Fact]
    public void Chains_scope_to_descendants()
    {
        var tree = SampleTree();
        Assert.Equal(2, SelectorMatcher.Match(SelectorParser.Parse("button \"OK\""), tree).Count);
        Assert.Equal("ok2", Assert.Single(SelectorMatcher.Match(SelectorParser.Parse("window \"Confirm\" >> button \"OK\""), tree)).AutomationId);
    }

    [Fact]
    public void Nth_is_one_based_and_out_of_range_matches_nothing()
    {
        var tree = SampleTree();
        Assert.Equal("ok2", Assert.Single(SelectorMatcher.Match(SelectorParser.Parse("button \"OK\":nth(2)"), tree)).AutomationId);
        Assert.Empty(SelectorMatcher.Match(SelectorParser.Parse("button \"OK\":nth(3)"), tree));
    }

    [Fact]
    public void Text_role_does_not_match_inputs_with_same_name()
    {
        var tree = SampleTree();
        Assert.Equal(ControlTypeIds.Text, Assert.Single(SelectorMatcher.Match(SelectorParser.Parse("text \"Name:\""), tree)).ControlTypeId);
    }
}

public class SelectorBuilderTests
{
    [Fact]
    public void Prefers_unique_automation_id()
    {
        var target = TextBox("Name", "txtName");
        var tree = Tree(Window("Main", Pane(target)));
        Assert.Equal("#txtName", SelectorBuilder.Build(target, tree).ToString());
    }

    [Fact]
    public void Falls_back_to_role_and_name()
    {
        var target = Button("Save");
        var tree = Tree(Window("Main", Pane(target, Button("Cancel"))));
        Assert.Equal("button \"Save\"", SelectorBuilder.Build(target, tree).ToString());
    }

    [Fact]
    public void Scopes_duplicates_under_an_identifiable_ancestor()
    {
        var target = Button("OK");
        var tree = Tree(
            Window("Main", Button("OK")),
            Window("Confirm", target));
        var selector = SelectorBuilder.Build(target, tree);
        Assert.Equal("window \"Confirm\" >> button \"OK\"", selector.ToString());
        Assert.Same(target, Assert.Single(SelectorMatcher.Match(selector, tree)));
    }

    [Fact]
    public void Uses_position_when_nothing_else_is_unique()
    {
        var first = Button("Row");
        var second = Button("Row");
        var tree = Tree(Window("Main", first, second));
        var selector = SelectorBuilder.Build(second, tree);
        Assert.Same(second, Assert.Single(SelectorMatcher.Match(selector, tree)));
        Assert.EndsWith(":nth(2)", selector.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Ignores_numeric_automation_ids_generated_by_the_framework()
    {
        var target = Button("Save", "1234");
        var tree = Tree(Window("Main", target));
        Assert.Equal("button \"Save\"", SelectorBuilder.Build(target, tree).ToString());
    }
}
