using System.Globalization;
using Netwright.Engine.Model;
using Netwright.Engine.Uia;

namespace Netwright.Engine.Refs;

/// <summary>What the registry remembers about the element behind a Ref.</summary>
public sealed record RefEntry(string Ref, string Key, string Role, string Name, int ControlTypeId);

/// <summary>
/// Issues Refs that stay the same across Snapshots for as long as the element exists. A Ref is
/// bound to the element's UI Automation RuntimeId, so the same element always gets the same Ref.
/// </summary>
public sealed class RefRegistry
{
    private readonly Dictionary<string, string> _refByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RefEntry> _entries = new(StringComparer.Ordinal);
    private int _next = 1;

    public int Count => _entries.Count;

    public static bool LooksLikeRef(string text) =>
        text.Length >= 2 && text[0] == 'e' && text.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0;

    public string GetOrAssign(UiNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!_refByKey.TryGetValue(node.Key, out var id))
        {
            id = "e" + _next++.ToString(CultureInfo.InvariantCulture);
            _refByKey[node.Key] = id;
        }

        _entries[id] = new RefEntry(id, node.Key, node.Role, node.Name, node.ControlTypeId);
        return id;
    }

    public string? TryGetRef(UiNode node) => _refByKey.GetValueOrDefault(node.Key);

    public RefEntry? Lookup(string refId) => _entries.GetValueOrDefault(refId);

    /// <summary>
    /// Finds the element behind a Ref in a freshly captured tree, or explains why it is stale.
    /// Virtualized lists recycle item containers, so an item whose text changed under the same
    /// RuntimeId is treated as a different element.
    /// </summary>
    public UiNode Resolve(string refId, UiTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);
        var entry = Lookup(refId) ?? throw new NetwrightException(
            ErrorCodes.ElementNotFound,
            $"Ref '{refId}' was never issued in this session.",
            "Take a desktop_snapshot to get Refs, or use a Selector such as #automationId.");

        var node = tree.Find(entry.Key);
        var recycled = node is not null &&
            entry.ControlTypeId is ControlTypeIds.ListItem or ControlTypeIds.DataItem or ControlTypeIds.TreeItem &&
            !string.Equals(node.Name, entry.Name, StringComparison.Ordinal);

        if (node is null || recycled)
        {
            throw new NetwrightException(
                ErrorCodes.StaleRef,
                $"Ref '{refId}' ({entry.Role} \"{entry.Name}\") no longer exists in the Target App.",
                "The UI changed. Take a new desktop_snapshot or target the element with a Selector.");
        }

        return node;
    }

    public void Clear()
    {
        _refByKey.Clear();
        _entries.Clear();
        _next = 1;
    }
}
