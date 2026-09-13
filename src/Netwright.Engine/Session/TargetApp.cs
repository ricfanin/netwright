using System.Diagnostics;
using Netwright.Engine.Uia;

namespace Netwright.Engine.Session;

/// <summary>
/// The Target App of a session: one process plus any child processes it starts, whose windows
/// belong to the same app from the Agent's point of view.
/// </summary>
internal sealed class TargetApp : IDisposable
{
    private readonly object _lock = new();
    private IReadOnlyCollection<int> _processIds;
    private DateTime _processIdsRefreshedAt;

    public TargetApp(Process process, string executablePath, bool launchedBySession)
    {
        Process = process;
        RootProcessId = process.Id;
        ExecutablePath = executablePath;
        LaunchedBySession = launchedBySession;
        _processIds = [process.Id];
    }

    public Process Process { get; }

    public int RootProcessId { get; }

    public string ExecutablePath { get; }

    public string ExecutableName => Path.GetFileName(ExecutablePath);

    public bool LaunchedBySession { get; }

    /// <summary>Set when the Agent asked to close the app, so its exit is not reported as a crash.</summary>
    public bool CloseRequested { get; set; }

    public bool HasExited
    {
        get
        {
            try
            {
                return Process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    /// <summary>The root process and every live descendant, refreshed at most twice a second.</summary>
    public IReadOnlyCollection<int> ProcessIds
    {
        get
        {
            lock (_lock)
            {
                if (DateTime.UtcNow - _processIdsRefreshedAt < TimeSpan.FromMilliseconds(500))
                {
                    return _processIds;
                }

                var parents = NativeMethods.ProcessParents();
                var ids = new HashSet<int> { RootProcessId };
                bool added;
                do
                {
                    added = false;
                    foreach (var (pid, parent) in parents)
                    {
                        if (ids.Contains(parent) && pid != parent && ids.Add(pid))
                        {
                            added = true;
                        }
                    }
                }
                while (added);

                _processIds = ids;
                _processIdsRefreshedAt = DateTime.UtcNow;
                return _processIds;
            }
        }
    }

    public bool Owns(int processId) => ProcessIds.Contains(processId);

    public void Dispose() => Process.Dispose();
}
