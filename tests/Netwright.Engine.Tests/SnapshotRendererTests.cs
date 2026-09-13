using Netwright.Engine.Model;
using Netwright.Engine.Refs;
using Netwright.Engine.Snapshots;
using Netwright.Engine.Uia;
using static Netwright.Engine.Tests.TestTree;

namespace Netwright.Engine.Tests;

public class SnapshotRendererTests
{
    private static SnapshotRenderer Renderer(SnapshotOptions? options = null) => new(new RefRegistry(), options ?? new SnapshotOptions());

    [Fact]
    public void Collapses_unnamed_panes_and_indents_named_elements()
    {
        var window = Window("Orders",
            Pane(Pane(
                TextBox("Customer", "txtCustomer", value: "Rossi"),
                Button("Save", "btnSave", enabled: false))));

        var text = Renderer().Render([window]).Text;

        Assert.Equal(
            """
            - window "Orders" [e1]
              - textbox "Customer" [e2] #txtCustomer value="Rossi"
              - button "Save" [e3] #btnSave disabled
            """.ReplaceLineEndings(),
            text.ReplaceLineEndings());
    }

    [Fact]
    public void Drops_text_that_repeats_its_parent_name_and_gives_text_no_ref()
    {
        var button = Node(ControlTypeIds.Button, "Submit", patterns: UiPatterns.Invoke, children: Text("Submit"));
        var window = Window("Main", button, Text("Result: ok", "lblResult"));

        var text = Renderer().Render([window]).Text;

        Assert.DoesNotContain("text \"Submit\"", text, StringComparison.Ordinal);
        Assert.Contains("- text \"Result: ok\" #lblResult", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_toggle_selection_expansion_and_range_states()
    {
        var window = Window("Main",
            Node(ControlTypeIds.CheckBox, "Accept", patterns: UiPatterns.Toggle, toggle: ToggleValue.On),
            Node(ControlTypeIds.RadioButton, "Pro", patterns: UiPatterns.SelectionItem, selected: false),
            Node(ControlTypeIds.ComboBox, "Country", patterns: UiPatterns.ExpandCollapse, expand: ExpandValue.Collapsed),
            new UiNode
            {
                Key = "slider",
                ControlTypeId = ControlTypeIds.Slider,
                Role = "slider",
                Name = "Quantity",
                Patterns = UiPatterns.RangeValue,
                RangeValue = 3,
                RangeMinimum = 0,
                RangeMaximum = 10,
            });

        var text = Renderer().Render([window]).Text;

        Assert.Contains("checkbox \"Accept\" [e2] checked", text, StringComparison.Ordinal);
        Assert.Contains("radio \"Pro\" [e3] unchecked", text, StringComparison.Ordinal);
        Assert.Contains("combobox \"Country\" [e4] collapsed", text, StringComparison.Ordinal);
        Assert.Contains("slider \"Quantity\" [e5] value=3 (0-10)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Summarizes_rows_and_columns_and_limits_items()
    {
        var rows = Enumerable.Range(1, 30)
            .Select(i => Node(ControlTypeIds.DataItem, $"Row {i}", patterns: UiPatterns.SelectionItem,
                children: [Node(ControlTypeIds.Custom, value: i.ToString(System.Globalization.CultureInfo.InvariantCulture), patterns: UiPatterns.Value), Node(ControlTypeIds.Custom, value: $"Customer {i}", patterns: UiPatterns.Value)]))
            .ToArray();
        var header = Node(ControlTypeIds.Header, children: [Node(ControlTypeIds.HeaderItem, "Id"), Node(ControlTypeIds.HeaderItem, "Customer")]);
        var grid = Node(ControlTypeIds.DataGrid, "Orders", "gridOrders", UiPatterns.Grid, rows: 1000, children: [header, .. rows]);

        var text = Renderer(new SnapshotOptions { MaxItemsPerContainer = 5 }).Render([Window("Main", grid)]).Text;

        Assert.Contains("grid \"Orders\" [e2] #gridOrders rows=1000", text, StringComparison.Ordinal);
        Assert.Contains("- columns: Id | Customer", text, StringComparison.Ordinal);
        Assert.Contains("row \"1 | Customer 1\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Customer 6", text, StringComparison.Ordinal);
        Assert.Contains("... 25 more items", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Summarizes_winforms_grid_rows_made_of_custom_rows_and_data_item_cells()
    {
        var header = Node(ControlTypeIds.Custom, "Top Row", children: [Node(ControlTypeIds.Header, "Id"), Node(ControlTypeIds.Header, "Customer")]);
        var row = Node(ControlTypeIds.Custom, "Row 0",
            children: [Node(ControlTypeIds.DataItem, "Id Row 0", patterns: UiPatterns.Value, value: "1"), Node(ControlTypeIds.DataItem, "Customer Row 0", patterns: UiPatterns.Value, value: "Customer 1")]);
        var grid = Node(ControlTypeIds.DataGrid, "Orders", patterns: UiPatterns.Grid, rows: 1000, children: [header, row, Text("orphan cell")]);

        var text = Renderer().Render([Window("Main", grid)]).Text;

        Assert.Contains("- columns: Id | Customer", text, StringComparison.Ordinal);
        Assert.Contains("- row \"1 | Customer 1\" [e3]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Id Row 0", text, StringComparison.Ordinal);
        Assert.DoesNotContain("orphan cell", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Hides_the_parts_of_composite_controls()
    {
        var slider = Node(ControlTypeIds.Slider, "Quantity", patterns: UiPatterns.RangeValue, children: [Node(ControlTypeIds.Button, "", "IncreaseLarge", UiPatterns.Invoke)]);
        var combo = Node(ControlTypeIds.ComboBox, "Country", patterns: UiPatterns.ExpandCollapse, expand: ExpandValue.Expanded,
            children: [Node(ControlTypeIds.Button, "Open", patterns: UiPatterns.Invoke), Node(ControlTypeIds.List, children: [Node(ControlTypeIds.ListItem, "Italy", patterns: UiPatterns.SelectionItem)])]);

        var text = Renderer().Render([Window("Main", slider, combo)]).Text;

        Assert.DoesNotContain("IncreaseLarge", text, StringComparison.Ordinal);
        Assert.DoesNotContain("button \"Open\"", text, StringComparison.Ordinal);
        Assert.Contains("    - item \"Italy\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Marks_minimized_windows()
    {
        var window = Node(ControlTypeIds.Window, "Main", patterns: UiPatterns.Window, offscreen: true);

        Assert.Contains("window \"Main\" [e1] minimized", Renderer().Render([window]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Hides_offscreen_children_but_counts_them()
    {
        var list = Node(ControlTypeIds.Pane, "Long list", patterns: UiPatterns.Scroll,
            children: [Button("Item 1"), Button("Item 2"), Node(ControlTypeIds.Button, "Item 3", patterns: UiPatterns.Invoke, offscreen: true)]);

        var text = Renderer().Render([Window("Main", list)]).Text;

        Assert.DoesNotContain("Item 3", text, StringComparison.Ordinal);
        Assert.Contains("... 1 offscreen", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Refs_are_stable_across_renders()
    {
        var refs = new RefRegistry();
        var first = Window("Main", Button("A", key: "a"), Button("B", key: "b"));

        var renderer = new SnapshotRenderer(refs, new SnapshotOptions());
        renderer.Render([first]);
        var bRef = refs.TryGetRef(first.Children[1]);

        var again = Window("Main", Button("New", key: "n"), Button("B", key: "b"));
        renderer.Render([again]);

        Assert.Equal(bRef, refs.TryGetRef(again.Children[1]));
        Assert.NotEqual(bRef, refs.TryGetRef(again.Children[0]));
    }

    [Fact]
    public void Truncates_when_line_budget_is_exceeded()
    {
        var window = Window("Main", Enumerable.Range(1, 50).Select(i => Button($"B{i}")).ToArray());

        var result = Renderer(new SnapshotOptions { MaxLines = 10 }).Render([window]);

        Assert.Equal(10, result.Lines);
        Assert.Equal(41, result.OmittedElements);
        Assert.Contains("41 more elements not shown", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Full_mode_keeps_layout_elements()
    {
        var window = Window("Main", Pane(Node(ControlTypeIds.ScrollBar, "Vertical"), Button("Go")));

        var text = Renderer(new SnapshotOptions { Full = true }).Render([window]).Text;

        Assert.Contains("- pane", text, StringComparison.Ordinal);
        Assert.Contains("scrollbar \"Vertical\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Escapes_quotes_and_newlines_and_masks_passwords()
    {
        var password = new UiNode { Key = "pw", ControlTypeId = ControlTypeIds.Edit, Role = "textbox", Name = "Password", Patterns = UiPatterns.Value, Value = "secret", IsPassword = true };
        var window = Window("Main", Text("Say \"hi\"\nnow"), password);

        var text = Renderer().Render([window]).Text;

        Assert.Contains("text \"Say \\\"hi\\\"\\nnow\"", text, StringComparison.Ordinal);
        Assert.Contains("value=***", text, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", text, StringComparison.Ordinal);
    }
}
