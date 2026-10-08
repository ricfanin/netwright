using System.Runtime.InteropServices;

namespace Netwright.Companion;

/// <summary>
/// Keeps the Target App invisible: every top-level window of the UI thread is hidden just before it is
/// first shown, so it never appears on screen. The window keeps its size and position, so UI Automation
/// still reads it as on-screen and PrintWindow still renders it for screenshots (ADR 0008). Two ways
/// are needed:
/// <list type="bullet">
/// <item>a layered window with zero opacity that lets clicks through, removed from the taskbar. UI
/// Automation still sees child windows, which WinForms controls are;</item>
/// <item>a DWM cloak, for windows that cannot become layered (WPF). UI Automation skips child windows
/// of a cloaked window, but WPF draws its controls without child windows.</item>
/// </list>
/// </summary>
internal static class HiddenWindows
{
    public const string Variable = "NETWRIGHT_HIDDEN";

    private const int WhCallWndProc = 4;
    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const int WsChild = 0x40000000;
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const uint LwaAlpha = 0x2;
    private const uint WmShowWindow = 0x0018;
    private const uint WmWindowPosChanging = 0x0046;
    private const uint SwpShowWindow = 0x0040;
    private const int DwmwaCloak = 13;

    private static readonly object Lock = new();

    // Windows hidden as layered windows, with the extended style they had before.
    private static readonly Dictionary<nint, int> Layered = [];

    // Kept in a field so the delegate the hook calls is never collected.
    private static HookProc? s_hook;

    private delegate nint HookProc(int code, nint wParam, nint lParam);

    /// <summary>Called on the main thread before Main, which is the UI thread of WPF and WinForms apps.</summary>
    public static void InstallIfRequested()
    {
        if (Environment.GetEnvironmentVariable(Variable) != "1")
        {
            return;
        }

        s_hook = OnCallWndProc;
        SetWindowsHookEx(WhCallWndProc, s_hook, 0, GetCurrentThreadId());
    }

    /// <summary>Shows a hidden window for a Foreground Action (<paramref name="hidden"/> false), or hides it again.</summary>
    public static bool SetHidden(nint window, bool hidden) => hidden ? Hide(window) : Show(window);

    private static bool Hide(nint window)
    {
        lock (Lock)
        {
            var exStyle = GetWindowLong(window, GwlExStyle);
            var original = Layered.TryGetValue(window, out var known) ? known : exStyle;
            _ = SetWindowLong(window, GwlExStyle, original | WsExLayered | WsExTransparent);
            if (SetLayeredWindowAttributes(window, 0, 0, LwaAlpha))
            {
                Layered[window] = original;
                RemoveTaskbarButtonSoon(window);
                return true;
            }

            _ = SetWindowLong(window, GwlExStyle, original);
            var cloak = 1;
            return DwmSetWindowAttribute(window, DwmwaCloak, ref cloak, sizeof(int)) == 0;
        }
    }

    private static bool Show(nint window)
    {
        lock (Lock)
        {
            if (Layered.TryGetValue(window, out var original))
            {
                _ = SetWindowLong(window, GwlExStyle, original);
                return true;
            }

            var cloak = 0;
            return DwmSetWindowAttribute(window, DwmwaCloak, ref cloak, sizeof(int)) == 0;
        }
    }

    // The shell adds the taskbar button once the window is visible, so it is removed a moment later.
    private static void RemoveTaskbarButtonSoon(nint window) =>
        _ = Task.Delay(300).ContinueWith(_ =>
        {
            try
            {
                var taskbar = (ITaskbarList)new TaskbarList();
                taskbar.HrInit();
                taskbar.DeleteTab(window);
            }
            catch (Exception)
            {
            }
        }, TaskScheduler.Default);

    // Runs before the window procedure sees the message: the window exists but is not visible yet.
    private static nint OnCallWndProc(int code, nint wParam, nint lParam)
    {
        try
        {
            var message = Marshal.PtrToStructure<CallWndProcMessage>(lParam);
            var showing = message.Message == WmShowWindow
                ? message.WParam != 0
                : message.Message == WmWindowPosChanging && message.LParam != 0
                    && (Marshal.PtrToStructure<WindowPos>(message.LParam).Flags & SwpShowWindow) != 0;
            if (code >= 0 && showing && (GetWindowLong(message.Window, GwlStyle) & WsChild) == 0)
            {
                Hide(message.Window);
            }
        }
        catch (Exception)
        {
        }

        return CallNextHookEx(0, code, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CallWndProcMessage
    {
        public nint LParam;
        public nint WParam;
        public uint Message;
        public nint Window;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public nint Window;
        public nint InsertAfter;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public uint Flags;
    }

    [ComImport]
    [Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        void HrInit();

        void AddTab(nint hwnd);

        void DeleteTab(nint hwnd);

        void ActivateTab(nint hwnd);

        void SetActiveAlt(nint hwnd);
    }

    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarList
    {
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(nint hwnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(nint hwnd, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);
}
