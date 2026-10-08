using Netwright.Engine.Uia;

namespace Netwright.Engine.Input;

/// <summary>
/// Temporarily brings a Target App window to the foreground for a Foreground Action, then gives
/// focus and the mouse cursor back to wherever the User left them. As a guard around a Background
/// Action it only gives the foreground back if the Target App took it.
/// </summary>
internal sealed class ForegroundScope : IDisposable
{
    private readonly nint _previousWindow;
    private readonly NativeMethods.Point _previousCursor;
    private readonly bool _hadCursor;
    private readonly Func<nint, bool>? _restoreOnlyFrom;
    private bool _disposed;

    private ForegroundScope(nint previousWindow, NativeMethods.Point previousCursor, bool hadCursor, Func<nint, bool>? restoreOnlyFrom = null)
    {
        _previousWindow = previousWindow;
        _previousCursor = previousCursor;
        _hadCursor = hadCursor;
        _restoreOnlyFrom = restoreOnlyFrom;
    }

    /// <summary>True when disposing gave the foreground back to the User's window.</summary>
    public bool Restored { get; private set; }

    /// <summary>
    /// Watches a Background Action. Cross-process UI Automation calls let the Target App take the
    /// foreground (ADR 0007), so on dispose the foreground goes back to the User's window, but only if
    /// a window for which <paramref name="ownedByTargetApp"/> is true took it. The cursor is never moved.
    /// </summary>
    public static ForegroundScope Guard(Func<nint, bool> ownedByTargetApp) =>
        new(NativeMethods.GetForegroundWindow(), default, hadCursor: false, ownedByTargetApp);

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

        GiveBack();
    }

    /// <summary>Gives the foreground back to the User's window now; safe to call repeatedly.</summary>
    public void GiveBack()
    {
        var current = NativeMethods.GetForegroundWindow();
        if (!_keepFocus && _previousWindow != 0 && NativeMethods.IsWindow(_previousWindow) && current != _previousWindow
            && (_restoreOnlyFrom is null || _restoreOnlyFrom(current)))
        {
            Restored |= Activate(_previousWindow);
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
