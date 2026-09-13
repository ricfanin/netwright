using System.Globalization;

namespace Netwright.Engine.Diagnostics;

public static class OutputStreams
{
    public const string Stdout = "stdout";
    public const string Stderr = "stderr";
    public const string Debug = "debug";
}

public sealed record OutputEntry(long Sequence, DateTimeOffset Time, string Stream, string Text)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"[{Time:HH:mm:ss.fff} {Stream}] {Text}");
}

/// <summary>
/// App Output of the Target App: standard output and error when Netwright launched it, and
/// debug/trace output in all cases. Keeps the most recent lines and remembers what the Agent has
/// already read, so repeated reads only return new lines.
/// </summary>
public sealed class AppOutput
{
    private const int Capacity = 5000;
    private readonly LinkedList<OutputEntry> _entries = new();
    private readonly object _lock = new();
    private long _nextSequence = 1;
    private long _readUpTo;

    public void Add(string stream, string text)
    {
        lock (_lock)
        {
            _entries.AddLast(new OutputEntry(_nextSequence++, DateTimeOffset.Now, stream, text));
            while (_entries.Count > Capacity)
            {
                _entries.RemoveFirst();
            }
        }
    }

    /// <summary>Returns entries, by default only those not returned before, newest last.</summary>
    public (IReadOnlyList<OutputEntry> Entries, int Skipped) Read(string? stream, int max, bool includeAlreadyRead)
    {
        lock (_lock)
        {
            var matching = _entries
                .Where(e => includeAlreadyRead || e.Sequence > _readUpTo)
                .Where(e => stream is null || e.Stream == stream)
                .ToList();

            if (_entries.Count > 0)
            {
                _readUpTo = _entries.Last!.Value.Sequence;
            }

            var skipped = Math.Max(0, matching.Count - max);
            return (matching.Skip(skipped).ToList(), skipped);
        }
    }

    public IReadOnlyList<string> Tail(string stream, int lines)
    {
        lock (_lock)
        {
            return _entries.Where(e => e.Stream == stream).TakeLast(lines).Select(e => e.Text).ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _readUpTo = 0;
        }
    }
}
