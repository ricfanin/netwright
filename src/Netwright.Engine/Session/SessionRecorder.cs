namespace Netwright.Engine.Session;

/// <summary>One step an Agent performed successfully, expressed with stable Selectors.</summary>
public sealed record RecordedStep(string Kind, string? Selector, IReadOnlyDictionary<string, string?> Arguments)
{
    public string? Argument(string name) => Arguments.GetValueOrDefault(name);
}

/// <summary>
/// Keeps the successful actions and Expectations of a session so they can become a Test Export.
/// </summary>
public sealed class SessionRecorder
{
    private readonly List<RecordedStep> _steps = [];
    private readonly object _lock = new();

    public IReadOnlyList<RecordedStep> Steps
    {
        get
        {
            lock (_lock)
            {
                return _steps.ToList();
            }
        }
    }

    public void Record(string kind, string? selector, params (string Name, string? Value)[] arguments)
    {
        var step = new RecordedStep(kind, selector, arguments.Where(a => a.Value is not null).ToDictionary(a => a.Name, a => a.Value));
        lock (_lock)
        {
            _steps.Add(step);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _steps.Clear();
        }
    }
}
