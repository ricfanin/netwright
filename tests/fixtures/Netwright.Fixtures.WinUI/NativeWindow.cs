using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Netwright.Fixtures.WinUI;

/// <summary>Small Win32 helpers WinUI does not expose: owner windows, DIP sizing and centering.</summary>
internal static partial class NativeWindow
{
    private const int GWLP_HWNDPARENT = -8;

    public static IntPtr Handle(Window window) => WinRT.Interop.WindowNative.GetWindowHandle(window);

    /// <summary>Makes <paramref name="window"/> an owned window of <paramref name="owner"/> (like WPF Window.Owner).</summary>
    public static void SetOwner(Window window, Window owner) =>
        SetWindowLongPtr(Handle(window), GWLP_HWNDPARENT, Handle(owner));

    /// <summary>Resizes to a size in device-independent pixels (96 DPI units, like WPF Width/Height).</summary>
    public static void ResizeDips(Window window, int width, int height)
    {
        var scale = GetDpiForWindow(Handle(window)) / 96.0;
        window.AppWindow.Resize(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
    }

    public static void CenterOnScreen(Window window)
    {
        var area = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        CenterIn(window, area);
    }

    public static void CenterOnOwner(Window window, Window owner)
    {
        var position = owner.AppWindow.Position;
        var size = owner.AppWindow.Size;
        CenterIn(window, new RectInt32(position.X, position.Y, size.Width, size.Height));
    }

    /// <summary>Enables or disables input to a window (what WPF ShowDialog does to the owner).</summary>
    public static void SetEnabled(Window window, bool enabled) => EnableWindow(Handle(window), enabled);

    /// <summary>Double-click interval in milliseconds, as configured in Windows.</summary>
    public static uint DoubleClickTime() => GetDoubleClickTime();

    private static void CenterIn(Window window, RectInt32 area)
    {
        var size = window.AppWindow.Size;
        window.AppWindow.Move(new PointInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2));
    }

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial uint GetDoubleClickTime();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnableWindow(IntPtr hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);
}
