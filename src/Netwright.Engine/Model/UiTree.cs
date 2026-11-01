namespace Netwright.Engine.Model;

/// <summary>
/// All top-level windows of the Target App captured at one instant.
/// </summary>
public sealed class UiTree
{
    private Dictionary<string, UiNode>? _byKey;

    public UiTree(IReadOnlyList<UiNode> windows, DateTimeOffset capturedAt, bool includesOffscreen = true, bool isLight = false)
    {
        Windows = windows;
        CapturedAt = capturedAt;
        IncludesOffscreen = includesOffscreen;
        IsLight = isLight;
    }

    public IReadOnlyList<UiNode> Windows { get; }

    public DateTimeOffset CapturedAt { get; }

    /// <summary>False when offscreen elements were filtered out at capture time to keep large UIs fast.</summary>
    public bool IncludesOffscreen { get; }

    /// <summary>True for polling captures whose nodes cannot be acted on.</summary>
    public bool IsLight { get; }

    public IEnumerable<UiNode> AllNodes => Windows.SelectMany(w => w.DescendantsAndSelf());

    public UiNode? Find(string key)
    {
        _byKey ??= AllNodes.GroupBy(n => n.Key).ToDictionary(g => g.Key, g => g.First());
        return _byKey.GetValueOrDefault(key);
    }

    /// <summary>
    /// The element with keyboard focus. While a modal window is open it blocks input to the rest of
    /// the app, so focus is looked for inside that window only: providers that keep a focused element
    /// per window would otherwise name an element behind the dialog. Null when none is found there.
    /// </summary>
    public UiNode? Focused => (ActiveModal?.DescendantsAndSelf() ?? AllNodes).LastOrDefault(n => n.HasFocus);

    /// <summary>The innermost open modal window (the last one in document order), or null.</summary>
    public UiNode? ActiveModal => AllNodes.LastOrDefault(n => n.ControlTypeId == Uia.ControlTypeIds.Window && n.IsModal == true);

    /// <summary>
    /// A cheap fingerprint of everything a Change Report can mention. Two equal fingerprints mean the
    /// UI did not change in a way the Agent could observe.
    /// </summary>
    public long Fingerprint()
    {
        var hash = new HashCode();
        foreach (var node in AllNodes)
        {
            hash.Add(node.Key);
            hash.Add(node.Name);
            hash.Add(node.Value);
            hash.Add(node.IsEnabled);
            hash.Add(node.IsOffscreen);
            hash.Add(node.HasFocus);
            hash.Add(node.Toggle);
            hash.Add(node.IsSelected);
            hash.Add(node.Expand);
            hash.Add(node.RangeValue);
            hash.Add(node.Children.Count);
        }

        return hash.ToHashCode();
    }

    public static UiTree Empty { get; } = new([], DateTimeOffset.MinValue);
}
