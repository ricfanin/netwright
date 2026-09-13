using System.Drawing;
using System.Runtime.InteropServices;

namespace Netwright.Engine.Input;

/// <summary>Virtual-key codes used by Foreground Actions.</summary>
public enum VirtualKeyShort : ushort
{
    BACK = 0x08,
    TAB = 0x09,
    RETURN = 0x0D,
    SHIFT = 0x10,
    CONTROL = 0x11,
    ALT = 0x12,
    ESCAPE = 0x1B,
    SPACE = 0x20,
    PRIOR = 0x21,
    NEXT = 0x22,
    END = 0x23,
    HOME = 0x24,
    LEFT = 0x25,
    UP = 0x26,
    RIGHT = 0x27,
    DOWN = 0x28,
    INSERT = 0x2D,
    DELETE = 0x2E,
    KEY_0 = 0x30, KEY_1, KEY_2, KEY_3, KEY_4, KEY_5, KEY_6, KEY_7, KEY_8, KEY_9,
    KEY_A = 0x41, KEY_B, KEY_C, KEY_D, KEY_E, KEY_F, KEY_G, KEY_H, KEY_I, KEY_J, KEY_K, KEY_L, KEY_M,
    KEY_N, KEY_O, KEY_P, KEY_Q, KEY_R, KEY_S, KEY_T, KEY_U, KEY_V, KEY_W, KEY_X, KEY_Y, KEY_Z,
    LWIN = 0x5B,
    APPS = 0x5D,
    F1 = 0x70, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    F13, F14, F15, F16, F17, F18, F19, F20, F21, F22, F23, F24,
}

public enum MouseButton
{
    Left,
    Right,
    Middle,
}

/// <summary>Real mouse input through <c>SendInput</c>; only used inside a Foreground Action.</summary>
internal static class Mouse
{
    private const uint LeftDown = 0x0002;
    private const uint LeftUp = 0x0004;
    private const uint RightDown = 0x0008;
    private const uint RightUp = 0x0010;
    private const uint MiddleDown = 0x0020;
    private const uint MiddleUp = 0x0040;

    public static void MoveTo(Point point) => NativeInput.SetCursorPos(point.X, point.Y);

    public static void Click(Point point, MouseButton button = MouseButton.Left)
    {
        MoveTo(point);
        var (down, up) = button switch
        {
            MouseButton.Right => (RightDown, RightUp),
            MouseButton.Middle => (MiddleDown, MiddleUp),
            _ => (LeftDown, LeftUp),
        };
        NativeInput.Send([NativeInput.MouseInput(down), NativeInput.MouseInput(up)]);
    }

    public static void DoubleClick(Point point, MouseButton button = MouseButton.Left)
    {
        Click(point, button);
        Thread.Sleep(40);
        Click(point, button);
    }
}

/// <summary>Real keyboard input through <c>SendInput</c>; only used inside a Foreground Action.</summary>
internal static class Keyboard
{
    private const uint ExtendedKey = 0x0001;
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;

    private static readonly HashSet<VirtualKeyShort> Extended =
    [
        VirtualKeyShort.PRIOR, VirtualKeyShort.NEXT, VirtualKeyShort.END, VirtualKeyShort.HOME,
        VirtualKeyShort.LEFT, VirtualKeyShort.UP, VirtualKeyShort.RIGHT, VirtualKeyShort.DOWN,
        VirtualKeyShort.INSERT, VirtualKeyShort.DELETE, VirtualKeyShort.LWIN, VirtualKeyShort.APPS,
    ];

    /// <summary>Types text as Unicode characters, independent of the keyboard layout.</summary>
    public static void Type(string text)
    {
        var inputs = new List<NativeInput.Input>(text.Length * 2);
        foreach (var c in text)
        {
            if (c == '\n')
            {
                inputs.Add(Key(VirtualKeyShort.RETURN, 0));
                inputs.Add(Key(VirtualKeyShort.RETURN, KeyUp));
                continue;
            }

            if (c == '\r')
            {
                continue;
            }

            inputs.Add(NativeInput.KeyboardInput(0, c, Unicode));
            inputs.Add(NativeInput.KeyboardInput(0, c, Unicode | KeyUp));
        }

        NativeInput.Send([.. inputs]);
    }

    public static void Type(VirtualKeyShort key) => NativeInput.Send([Key(key, 0), Key(key, KeyUp)]);

    /// <summary>Presses keys in order and releases them in reverse, e.g. Ctrl+Shift+S.</summary>
    public static void TypeSimultaneously(params VirtualKeyShort[] keys)
    {
        // Enumerable.Reverse explicitly: on arrays, keys.Reverse() can bind to the in-place span overload.
        var inputs = keys.Select(k => Key(k, 0)).Concat(Enumerable.Reverse(keys).Select(k => Key(k, KeyUp))).ToArray();
        NativeInput.Send(inputs);
    }

    private static NativeInput.Input Key(VirtualKeyShort key, uint flags) =>
        NativeInput.KeyboardInput((ushort)key, '\0', flags | (Extended.Contains(key) ? ExtendedKey : 0));
}

internal static class Wait
{
    /// <summary>Gives the Target App a moment to pull the injected input from its queue.</summary>
    public static void UntilInputIsProcessed() => Thread.Sleep(80);
}

internal static partial class NativeInput
{
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInputData Mouse;

        [FieldOffset(0)]
        public KeyboardInputData Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInputData
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInputData
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    public static Input MouseInput(uint flags) => new() { Type = InputMouse, Data = new InputUnion { Mouse = new MouseInputData { Flags = flags } } };

    public static Input KeyboardInput(ushort virtualKey, char scanCode, uint flags) =>
        new() { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInputData { VirtualKey = virtualKey, ScanCode = scanCode, Flags = flags } } };

    public static void Send(Input[] inputs)
    {
        if (inputs.Length == 0)
        {
            return;
        }

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new NetwrightException(
                ErrorCodes.NeedsForeground,
                "Windows blocked the simulated input (another program may be running elevated or the workstation is locked).",
                "Make sure the Target App is not elevated relative to Netwright and the session is unlocked.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);
}
