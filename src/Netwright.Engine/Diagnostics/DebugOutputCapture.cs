using System.Runtime.InteropServices;
using System.Text;
using Netwright.Engine.Uia;

namespace Netwright.Engine.Diagnostics;

/// <summary>
/// Receives <c>OutputDebugString</c> output (which includes <c>Trace.WriteLine</c> and
/// <c>Debug.WriteLine</c>) from other processes in this session, through the same shared-memory
/// protocol DebugView uses. It needs no privileges and no changes to the Target App. Output only
/// reaches us when no debugger is attached to the Target App.
/// </summary>
internal sealed class DebugOutputCapture : IDisposable
{
    private const int BufferSize = 4096;

    private readonly nint _bufferReady;
    private readonly nint _dataReady;
    private readonly nint _mapping;
    private readonly nint _view;
    private readonly Func<int, bool> _acceptProcess;
    private readonly Action<int, string> _onLine;
    private readonly Thread _thread;
    private readonly Dictionary<int, StringBuilder> _partialLines = [];
    private volatile bool _stopping;

    private DebugOutputCapture(nint bufferReady, nint dataReady, nint mapping, nint view, Func<int, bool> acceptProcess, Action<int, string> onLine)
    {
        _bufferReady = bufferReady;
        _dataReady = dataReady;
        _mapping = mapping;
        _view = view;
        _acceptProcess = acceptProcess;
        _onLine = onLine;
        _thread = new Thread(Loop) { IsBackground = true, Name = "Netwright debug output" };
        _thread.Start();
    }

    /// <summary>Null when a debug listener could not be started; <paramref name="warning"/> explains why.</summary>
    public static DebugOutputCapture? TryStart(Func<int, bool> acceptProcess, Action<int, string> onLine, out string? warning)
    {
        warning = null;
        var bufferReady = NativeMethods.CreateEvent(0, false, false, "DBWIN_BUFFER_READY");
        var alreadyListening = Marshal.GetLastWin32Error() == NativeMethods.ErrorAlreadyExists;
        var dataReady = NativeMethods.CreateEvent(0, false, false, "DBWIN_DATA_READY");
        var mapping = NativeMethods.CreateFileMapping(-1, 0, NativeMethods.PageReadWrite, 0, BufferSize, "DBWIN_BUFFER");
        var view = mapping == 0 ? 0 : NativeMethods.MapViewOfFile(mapping, NativeMethods.FileMapRead, 0, 0, BufferSize);

        if (bufferReady == 0 || dataReady == 0 || mapping == 0 || view == 0)
        {
            Close(bufferReady, dataReady, mapping, view);
            warning = "Debug output capture is unavailable on this machine.";
            return null;
        }

        if (alreadyListening)
        {
            warning = "Another debug output listener (DebugView or a debugger) is running, so some debug lines may be missed.";
        }

        return new DebugOutputCapture(bufferReady, dataReady, mapping, view, acceptProcess, onLine);
    }

    private void Loop()
    {
        while (!_stopping)
        {
            NativeMethods.SetEvent(_bufferReady);
            if (NativeMethods.WaitForSingleObject(_dataReady, 250) != NativeMethods.WaitObject0)
            {
                continue;
            }

            var pid = Marshal.ReadInt32(_view);
            if (!_acceptProcess(pid))
            {
                continue;
            }

            var text = Marshal.PtrToStringAnsi(_view + 4) ?? "";
            Append(pid, text);
        }
    }

    // Trace.WriteLine can arrive as several OutputDebugString calls; emit whole lines only.
    private void Append(int pid, string text)
    {
        if (!_partialLines.TryGetValue(pid, out var partial))
        {
            _partialLines[pid] = partial = new StringBuilder();
        }

        partial.Append(text);
        var content = partial.ToString();
        var lastNewline = content.LastIndexOf('\n');
        if (lastNewline < 0)
        {
            if (partial.Length > BufferSize)
            {
                _onLine(pid, content);
                partial.Clear();
            }

            return;
        }

        foreach (var line in content[..lastNewline].Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length > 0)
            {
                _onLine(pid, trimmed);
            }
        }

        partial.Clear().Append(content[(lastNewline + 1)..]);
    }

    public void Dispose()
    {
        _stopping = true;
        _thread.Join(1000);
        Close(_bufferReady, _dataReady, _mapping, _view);
    }

    private static void Close(nint bufferReady, nint dataReady, nint mapping, nint view)
    {
        if (view != 0) NativeMethods.UnmapViewOfFile(view);
        if (mapping != 0) NativeMethods.CloseHandle(mapping);
        if (dataReady != 0) NativeMethods.CloseHandle(dataReady);
        if (bufferReady != 0) NativeMethods.CloseHandle(bufferReady);
    }
}
