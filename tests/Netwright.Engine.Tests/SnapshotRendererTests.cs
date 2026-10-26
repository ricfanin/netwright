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
    public void Quotes_automation_ids_with_spaces_so_they_work_as_selectors()
    {
        var window = Window("Main", Node(ControlTypeIds.Button, "Clear Filter", "Clear Filter Button", UiPatterns.Invoke));

        var text = Renderer().Render([window]).Text;

        Assert.Contains("button \"Clear Filter\" [e2] #\"Clear Filter Button\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_the_controls_inside_a_grid_row_under_its_summary()
    {
        // WPF DataGrid: an editable text cell has Invoke and Value itself; template cells hold controls.
        var row = Node(ControlTypeIds.DataItem, "Row 1", patterns: UiPatterns.SelectionItem, children:
        [
            Node(ControlTypeIds.Custom, patterns: UiPatterns.Invoke | UiPatterns.Value, value: "1"),
            Node(ControlTypeIds.Custom, children: Node(ControlTypeIds.Button, "Delete", patterns: UiPatterns.Invoke, children: Text("Delete"))),
            Node(ControlTypeIds.Custom, children: Node(ControlTypeIds.CheckBox, "Paid", patterns: UiPatterns.Toggle, toggle: ToggleValue.Off)),
        ]);
        var grid = Node(ControlTypeIds.DataGrid, "Orders", patterns: UiPatterns.Grid, rows: 1, children: row);

        var text = Renderer().Render([Window("Main", grid)]).Text;

        Assert.Matches(@"- row ""1 \| Delete \| Paid"" \[e\d+\]\r?\n\s+- button ""Delete"" \[e\d+\]\r?\n\s+- checkbox ""Paid"" \[e\d+\] unchecked", text);
        Assert.DoesNotContain("custom", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Shows_the_button_and_check_box_cells_of_a_winforms_grid_row()
    {
        var idCell = Node(ControlTypeIds.DataItem, "Id Row 0", patterns: UiPatterns.Value, value: "1");
        var openCell = Node(ControlTypeIds.Button, "Open Row 0", patterns: UiPatterns.Invoke | UiPatterns.Value, value: "Open");
        var paidCell = Node(ControlTypeIds.CheckBox, "Paid Row 0", patterns: UiPatterns.Toggle, toggle: ToggleValue.On);
        var grid = Node(ControlTypeIds.DataGrid, "Orders", patterns: UiPatterns.Grid, rows: 1,
            children: Node(ControlTypeIds.Custom, "Row 0", children: [idCell, openCell, paidCell]));

        var renderer = Renderer();
        var text = renderer.Render([Window("Main", grid)]).Text;

        Assert.Contains("- row \"1 | Open | Paid Row 0\"", text, StringComparison.Ordinal);
        Assert.Matches(@"- button ""Open Row 0"" \[e\d+\] value=""Open""", text);
        Assert.Matches(@"- checkbox ""Paid Row 0"" \[e\d+\] checked", text);
        Assert.DoesNotContain("Id Row 0", text, StringComparison.Ordinal);
        Assert.True(renderer.IsReportable(openCell));
        Assert.False(renderer.IsReportable(idCell));
    }

    [Fact]
    public void Shows_the_button_of_an_item_that_wraps_a_button()
    {
        // A dialog button bar built from an items control: each item is a DataItem holding one button.
        static UiNode Item(string name) => Node(ControlTypeIds.DataItem, name, patterns: UiPatterns.SelectionItem,
            children: Pane(Node(ControlTypeIds.Button, name, patterns: UiPatterns.Invoke, children: Text(name))));
        var note = Node(ControlTypeIds.DataItem, "Note", children:
        [
            Node(ControlTypeIds.Edit, "Comment", patterns: UiPatterns.Value, value: "fine"),
            Node(ControlTypeIds.Edit, "Created", patterns: UiPatterns.Value, value: "today", readOnly: true),
        ]);
        var window = Window("Confirm", Node(ControlTypeIds.List, children: [Item("_Ja"), Item("_Nein"), note]));

        var renderer = Renderer();
        var text = renderer.Render([window]).Text;

        Assert.Matches(@"- row ""_Ja"" \[e\d+\]\r?\n\s+- button ""_Ja"" \[e\d+\]", text);
        Assert.Matches(@"- button ""_Nein"" \[e\d+\]", text);
        Assert.Matches(@"- textbox ""Comment"" \[e\d+\] value=""fine""", text);
        Assert.DoesNotContain("textbox \"Created\"", text, StringComparison.Ordinal);
        Assert.True(renderer.IsReportable(note.Children[0]));
        Assert.False(renderer.IsReportable(note.Children[1]));
    }

    [Fact]
    public void Keeps_rows_nested_in_a_row_as_rows_with_their_own_controls()
    {
        // Master-detail: an order row holds its own button and a list of line rows, each with a button.
        static UiNode Line(string name) => Node(ControlTypeIds.DataItem, name, patterns: UiPatterns.SelectionItem,
            children: [Text(name), Button("Remove")]);
        var order = Node(ControlTypeIds.DataItem, "Order 1", patterns: UiPatterns.SelectionItem, children:
        [
            Text("Order 1"),
            Button("Open"),
            Node(ControlTypeIds.List, "Lines", children: [Line("Line A"), Line("Line B"), Line("Line C")]),
        ]);
        var window = Window("Main", Node(ControlTypeIds.List, "Orders", children: order));

        var text = Renderer(new SnapshotOptions { MaxItemsPerContainer = 2 }).Render([window]).Text;

        Assert.Equal(
            """
            - window "Main" [e1]
              - list "Orders" [e2]
                - row "Order 1 | Open | Lines" [e3]
                  - button "Open" [e4]
                  - row "Line A | Remove" [e5]
                    - button "Remove" [e6]
                  - row "Line B | Remove" [e7]
                    - button "Remove" [e8]
                  - ... 1 more items
            """.ReplaceLineEndings(),
            text.ReplaceLineEndings());
    }

    [Fact]
    public void Shows_the_radio_buttons_a_row_holds()
    {
        // UI Automation radio buttons expose SelectionItem, not Toggle.
        static UiNode Option(string name, bool selected) => Node(ControlTypeIds.DataItem, name, patterns: UiPatterns.SelectionItem,
            children: Node(ControlTypeIds.RadioButton, name, patterns: UiPatterns.SelectionItem, selected: selected));
        var options = Node(ControlTypeIds.List, "Shipping", children: [Option("Standard", true), Option("Express", false)]);

        var renderer = Renderer();
        var text = renderer.Render([Window("Main", options)]).Text;

        Assert.Matches(@"- row ""Standard"" \[e\d+\]\r?\n\s+- radio ""Standard"" \[e\d+\] checked", text);
        Assert.Matches(@"- row ""Express"" \[e\d+\]\r?\n\s+- radio ""Express"" \[e\d+\] unchecked", text);
        Assert.True(renderer.IsReportable(options.Children[1].Children[0]));
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
