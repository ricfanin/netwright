using Netwright.Engine.Model;
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
    [InlineData("[value=\"ORD-4711\"]", null, null, "ORD-4711", NameMatch.Exact)]
    [InlineData("textbox[value~=\"@example.com\"]", "textbox", null, "@example.com", NameMatch.Contains)]
    [InlineData("#PositionNumber[value=\"ORD-4711\"]", null, "PositionNumber", "ORD-4711", NameMatch.Exact)]
    [InlineData("textbox [value=\"\"]", "textbox", null, "", NameMatch.Exact)]
    [InlineData("#Item[3]", null, "Item[3]", null, NameMatch.Exact)]
    public void Parses_value_predicates(string text, string? role, string? automationId, string? value, NameMatch match)
    {
        var step = Assert.Single(SelectorParser.Parse(text).Steps);

        Assert.Equal(role, step.Role);
        Assert.Equal(automationId, step.AutomationId);
        Assert.Equal(value, step.Value);
        Assert.Equal(match, step.ValueMatch);
    }

    [Fact]
    public void Parses_a_value_predicate_after_a_name_and_before_nth()
    {
        var step = SelectorParser.Parse("window \"Main\" >> textbox ~\"mail\"[value~=\"@\"]:nth(2)").Steps[1];

        Assert.Equal("mail", step.Name);
        Assert.Equal(NameMatch.Contains, step.NameMatch);
        Assert.Equal("@", step.Value);
        Assert.Equal(NameMatch.Contains, step.ValueMatch);
        Assert.Equal(2, step.Nth);
    }

    [Theory]
    [InlineData("ORD-4711")]
    [InlineData("quote\"inside")]
    [InlineData("back\\slash")]
    [InlineData("line\nbreak")]
    [InlineData("cr\r\nlf")]
    [InlineData("literal\\n")]
    [InlineData("]")]
    [InlineData("[value=\"x\"]")]
    [InlineData("")]
    public void Values_round_trip(string value)
    {
        foreach (var match in new[] { NameMatch.Exact, NameMatch.Contains })
        {
            var selector = new Selector([
                new SelectorStep("grid", null, "Orders", NameMatch.Exact, null),
                new SelectorStep("row", "Customer", "Row 1", NameMatch.Contains, 2) { Value = value, ValueMatch = match },
            ]);

            var printed = selector.ToString();

            Assert.DoesNotContain('\n', printed);
            Assert.Equal(selector.Steps, SelectorParser.Parse(printed).Steps);
        }
    }

    [Theory]
    [InlineData("[value=\"x\"]", "[value=\"x\"]")]
    [InlineData("textbox[value~=\"a \\\"b\\\"\"]", "textbox[value~=\"a \\\"b\\\"\"]")]
    [InlineData("#id \"Name\" [value=\"x\"]:nth(2)", "#id \"Name\"[value=\"x\"]:nth(2)")]
    public void Prints_value_predicates(string text, string printed)
    {
        Assert.Equal(printed, SelectorParser.Parse(text).ToString());
    }

    [Theory]
    [InlineData("#\"Clear Filter Button\"", "Clear Filter Button")]
    [InlineData("button#\"a \\\"b\\\" c\\\\d\"", "a \"b\" c\\d")]
    [InlineData("#\"x:y>z\" >> button", "x:y>z")]
    [InlineData("#\"Save\\r\\nButton\"", "Save\r\nButton")]
    [InlineData("#\"not\\\\n a break\"", "not\\n a break")]
    public void Parses_quoted_automation_ids(string text, string expected)
    {
        var step = SelectorParser.Parse(text).Steps[0];

        Assert.Equal(expected, step.AutomationId);
        Assert.Equal(step, SelectorParser.Parse(step.ToString()).Steps[0]);
    }

    [Theory]
    [InlineData("btnSave", "#btnSave")]
    [InlineData("Button.Yes", "#Button.Yes")]
    [InlineData("back\\slash", "#back\\slash")]
    [InlineData("Item 3", "#\"Item 3\"")]
    [InlineData("a\"b", "#\"a\\\"b\"")]
    [InlineData("C:\\temp", "#\"C:\\\\temp\"")]
    [InlineData("line\nbreak", "#\"line\\nbreak\"")]
    [InlineData("cr\r\nlf", "#\"cr\\r\\nlf\"")]
    [InlineData("Item[3]", "#\"Item[3]\"")]
    [InlineData("x[value=\"y\"]", "#\"x[value=\\\"y\\\"]\"")]
    public void Prints_automation_ids_that_parse_back(string id, string printed)
    {
        Assert.Equal(printed, SelectorStep.FormatAutomationId(id));
        Assert.Equal(id, SelectorParser.Parse(printed).Steps[0].AutomationId);
    }

    [Fact]
    public void An_empty_automation_id_cannot_be_printed()
    {
        Assert.Throws<ArgumentException>(() => SelectorStep.FormatAutomationId(""));
    }

    [Theory]
    [InlineData("Item 3")]
    [InlineData("tab\there")]
    [InlineData("line\nbreak")]
    [InlineData("cr\r\nlf")]
    [InlineData("literal\\n")]
    [InlineData("a>>b")]
    [InlineData("~tilde")]
    [InlineData("x:nth(2)")]
    [InlineData("quote\"inside")]
    [InlineData("trailing\\")]
    [InlineData("\\\"")]
    [InlineData("Ünïcødé id")]
    public void Selectors_with_awkward_automation_ids_round_trip(string id)
    {
        var selector = new Selector([
            new SelectorStep("window", null, "Main", NameMatch.Exact, null),
            new SelectorStep("button", id, "Save \"now\"", NameMatch.Contains, 2),
        ]);

        var parsed = SelectorParser.Parse(selector.ToString());

        Assert.Equal(selector.Steps, parsed.Steps);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#\"\"")]
    [InlineData("#\"open")]
    [InlineData("gizmo \"x\"")]
    [InlineData("button \"unterminated")]
    [InlineData("button >>")]
    [InlineData("button:nth(0)")]
    [InlineData("#")]
    [InlineData("~")]
    [InlineData("[value]")]
    [InlineData("[value=x]")]
    [InlineData("[value=\"x\"")]
    [InlineData("[value=\"x")]
    [InlineData("[value==\"x\"]")]
    [InlineData("[name=\"x\"]")]
    [InlineData("button[value=\"x\"] trailing")]
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
    [InlineData("#gridOrders >> #Customer[value=\"Customer 5\"]")]
    [InlineData("textbox[value~=\"@example.com\"]:nth(1)")]
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

    private static Model.UiTree OrdersTree() => Tree(
        Window("Main",
            Node(ControlTypeIds.DataGrid, "Orders", "gridOrders", children:
            [
                Node(ControlTypeIds.DataItem, "Row 1", children: [TextBox("", "Customer", value: "Ada Rossi"), TextBox("", "Order", value: "ORD-4711")]),
                Node(ControlTypeIds.DataItem, "Row 2", children: [TextBox("", "Customer", value: " ada rossi "), TextBox("", "Order", value: "ORD-4712")]),
            ]),
            Button("ORD-4711", "btnName"),
            TextBox("Email", "txtEmail", value: "ada@example.com"),
            Node(ControlTypeIds.Edit, "Password", "txtPassword", UiPatterns.Value, value: "secret", password: true)));

    [Fact]
    public void Matches_values_exactly_trimmed_and_case_insensitively()
    {
        var tree = OrdersTree();

        Assert.Equal(2, SelectorMatcher.Match(SelectorParser.Parse("[value=\"ADA ROSSI\"]"), tree).Count);
        Assert.Equal("Order", Assert.Single(SelectorMatcher.Match(SelectorParser.Parse("[value=\"ORD-4711\"]"), tree)).AutomationId);
        Assert.Empty(SelectorMatcher.Match(SelectorParser.Parse("[value=\"ORD-471\"]"), tree));
    }

    [Fact]
    public void Matches_values_that_contain_the_text()
    {
        var tree = OrdersTree();

        Assert.Equal(2, SelectorMatcher.Match(SelectorParser.Parse("#Order[value~=\"ord-47\"]"), tree).Count);
        Assert.Equal("txtEmail", Assert.Single(SelectorMatcher.Match(SelectorParser.Parse("textbox[value~=\"@example.com\"]"), tree)).AutomationId);
    }

    [Fact]
    public void A_value_predicate_combines_with_chains_and_nth()
    {
        var tree = OrdersTree();

        var match = Assert.Single(SelectorMatcher.Match(SelectorParser.Parse("#gridOrders >> #Customer[value=\"ada rossi\"]:nth(2)"), tree));

        Assert.Equal("Row 2", match.Parent!.Name);
    }

    [Fact]
    public void A_value_predicate_never_matches_an_element_without_a_value_or_a_password()
    {
        var tree = OrdersTree();

        Assert.DoesNotContain(SelectorMatcher.Match(SelectorParser.Parse("[value~=\"ORD-4711\"]"), tree), n => n.AutomationId == "btnName");
        Assert.Empty(SelectorMatcher.Match(SelectorParser.Parse("button[value~=\"\"]"), tree));
        Assert.Empty(SelectorMatcher.Match(SelectorParser.Parse("[value=\"secret\"]"), tree));
        Assert.Empty(SelectorMatcher.Match(SelectorParser.Parse("#txtPassword[value~=\"s\"]"), tree));
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

    [Fact]
    public void Quotes_an_automation_id_with_spaces_so_the_selector_parses_back()
    {
        // WPF gives list content ids like "Item 3"; printed unquoted, the Selector failed to parse.
        var target = Node(ControlTypeIds.Button, "", "Item 3", UiPatterns.Invoke);
        var tree = Tree(Window("Main", Button("Save"), target));

        var printed = SelectorBuilder.Build(target, tree).ToString();

        Assert.Equal("#\"Item 3\"", printed);
        Assert.Same(target, Assert.Single(SelectorMatcher.Match(SelectorParser.Parse(printed), tree)));
    }
}
