using Netwright.Engine.Uia;

namespace Netwright.Engine.Input;

/// <summary>
/// Temporarily brings a Target App window to the foreground for a Foreground Action, then gives
/// focus and the mouse cursor back to wherever the User left them.
/// </summary>
internal sealed class ForegroundScope : IDisposable
{
    private readonly nint _previousWindow;
    private readonly NativeMethods.Point _previousCursor;
    private readonly bool _hadCursor;
    private bool _disposed;

    private ForegroundScope(nint previousWindow, NativeMethods.Point previousCursor, bool hadCursor)
    {
        _previousWindow = previousWindow;
        _previousCursor = previousCursor;
        _hadCursor = hadCursor;
    }

    public static ForegroundScope Enter(nint targetWindow)
    {
        var previous = NativeMethods.GetForegroundWindow();
        var hadCursor = NativeMethods.GetCursorPos(out var cursor);
        var scope = new ForegroundScope(previous, cursor, hadCursor);

        if (targetWindow != 0)
        {
            if (NativeMethods.IsIconic(targetWindow))
            {
                NativeMethods.ShowWindow(targetWindow, NativeMethods.SwRestore);
            }

            if (!Activate(targetWindow))
            {
                throw new NetwrightException(
                    ErrorCodes.NeedsForeground,
                    "Windows refused to bring the Target App to the foreground.",
                    "Click the Target App window once, or retry; Windows blocks focus changes while the User is typing.");
            }
        }

        return scope;
    }

    private bool _keepFocus;

    /// <summary>Leaves focus on the Target App (the action opened a menu or dialog that would close on deactivation).</summary>
    public void KeepFocus() => _keepFocus = true;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_hadCursor)
        {
            NativeMethods.SetCursorPos(_previousCursor.X, _previousCursor.Y);
        }

        if (!_keepFocus && _previousWindow != 0 && NativeMethods.IsWindow(_previousWindow) && NativeMethods.GetForegroundWindow() != _previousWindow)
        {
            Activate(_previousWindow);
        }
    }

    /// <summary>
    /// Windows only lets the foreground thread change the foreground window, so we briefly attach
    /// our input queue to the current foreground thread before asking.
    /// </summary>
    private static bool Activate(nint window)
    {
        var root = NativeMethods.GetAncestor(window, 2 /* GA_ROOT */);
        if (root != 0)
        {
            window = root;
        }

        if (NativeMethods.GetForegroundWindow() == window)
        {
            return true;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        var foregroundThread = foreground == 0 ? 0 : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        var currentThread = NativeMethods.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != currentThread &&
            NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            NativeMethods.BringWindowToTop(window);
            NativeMethods.SetForegroundWindow(window);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }

        for (var i = 0; i < 20; i++)
        {
            if (NativeMethods.GetForegroundWindow() == window)
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }
}
