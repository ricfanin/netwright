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

    public UiNode? Focused => AllNodes.LastOrDefault(n => n.HasFocus);

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
