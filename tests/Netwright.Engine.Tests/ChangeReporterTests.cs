using Netwright.Engine.Model;
using Netwright.Engine.Refs;
using Netwright.Engine.Snapshots;
using Netwright.Engine.Uia;
using static Netwright.Engine.Tests.TestTree;

namespace Netwright.Engine.Tests;

public class ChangeReporterTests
{
    private readonly SnapshotRenderer _renderer = new(new RefRegistry(), new SnapshotOptions());

    private static UiNode Win(string title, string key, params UiNode[] children) =>
        Node(ControlTypeIds.Window, title, patterns: UiPatterns.Window, key: key, children: children);

    [Fact]
    public void Reports_no_changes_for_identical_trees()
    {
        var before = Tree(Win("Main", "w", Button("Save", key: "save")));
        var after = Tree(Win("Main", "w", Button("Save", key: "save")));

        var report = ChangeReporter.Build(before, after, _renderer);

        Assert.True(report.IsEmpty);
        Assert.Equal("", report.Text);
    }

    [Fact]
    public void Reports_an_opened_dialog_as_one_added_subtree()
    {
        var before = Tree(Win("Main", "w", Button("Delete", key: "del")));
        var dialog = Win("Confirm", "dlg", Text("Are you sure?", key: "t"), Button("Yes", key: "yes"), Button("No", key: "no"));
        var after = Tree(Win("Main", "w", Button("Delete", key: "del")), dialog);

        var report = ChangeReporter.Build(before, after, _renderer);

        Assert.Equal(1, report.Changes);
        var lines = report.Text.ReplaceLineEndings("\n").Split('\n');
        Assert.StartsWith("+ window \"Confirm\"", lines[0], StringComparison.Ordinal);
        Assert.Contains(lines, l => l.Contains("+ button \"Yes\"", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("+ text \"Are you sure?\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Reports_changed_state_removed_elements_and_focus()
    {
        var before = Tree(Win("Main", "w",
            Button("Submit", enabled: false, key: "submit"),
            Text("Loading...", key: "status"),
            TextBox("Name", key: "name")));
        var after = Tree(Win("Main", "w",
            Button("Submit", enabled: true, key: "submit"),
            TextBox("Name", key: "name", focused: true)));

        var text = ChangeReporter.Build(before, after, _renderer).Text;

        Assert.Contains("~ button \"Submit\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("disabled", text, StringComparison.Ordinal);
        Assert.Contains("- text \"Loading...\"", text, StringComparison.Ordinal);
        Assert.Contains("focus: textbox \"Name\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Element_becoming_visible_counts_as_added()
    {
        var before = Tree(Win("Main", "w", Node(ControlTypeIds.Button, "Later", patterns: UiPatterns.Invoke, offscreen: true, key: "later")));
        var after = Tree(Win("Main", "w", Button("Later", key: "later")));

        var report = ChangeReporter.Build(before, after, _renderer);

        Assert.Contains("+ button \"Later\"", report.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Refs_in_reports_match_refs_from_snapshots()
    {
        var refs = new RefRegistry();
        var renderer = new SnapshotRenderer(refs, new SnapshotOptions());
        var before = Tree(Win("Main", "w", Button("Submit", enabled: false, key: "submit")));
        var snapshot = renderer.Render(before.Windows).Text;
        var after = Tree(Win("Main", "w", Button("Submit", key: "submit")));

        var report = ChangeReporter.Build(before, after, renderer);

        Assert.Contains("button \"Submit\" [e2]", snapshot, StringComparison.Ordinal);
        Assert.Contains("~ button \"Submit\" [e2]", report.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Truncates_large_reports()
    {
        var before = Tree(Win("Main", "w"));
        var after = Tree(Win("Main", "w", Enumerable.Range(0, 20).Select(i => Button($"B{i}", key: $"b{i}")).ToArray()));

        var report = ChangeReporter.Build(before, after, _renderer, maxLines: 5);

        Assert.True(report.Truncated);
        Assert.Contains("15 more changes", report.Text, StringComparison.Ordinal);
    }
}
