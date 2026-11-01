using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Application = System.Windows.Application;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Netwright.Companion;

/// <summary>
/// Presses keys on a WPF element from inside the Target App by raising its routed key events, so a
/// Hidden App takes key presses without the foreground (ADR 0009). Each key raises PreviewKeyDown,
/// KeyDown (unless handled), the text input of a character key (unless the key-down was handled), and
/// PreviewKeyUp/KeyUp (KeyUp unless handled). The events
/// tunnel through the element's ancestors and bubble back, so window-level handlers and input bindings
/// see them. Nothing at the InputManager level runs: no Tab navigation, no access keys, no IME, and
/// <c>Keyboard.Modifiers</c> still reads the real keyboard.
/// </summary>
internal static class WpfKeys
{
    /// <param name="target">The element to press the keys on.</param>
    /// <param name="virtualKeys">Space-separated Windows virtual-key codes, one key per press.</param>
    /// <returns><c>ok</c>, <c>blocked</c> when the element's window is disabled (an open modal dialog
    /// keeps real keys from it too), or <c>unsupported ...</c>.</returns>
    public static string Press(UIElement target, string virtualKeys)
    {
        var keys = new List<Key>();
        foreach (var part in virtualKeys.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) ? KeyInterop.KeyFromVirtualKey(code) : Key.None;
            if (key == Key.None)
            {
                return "unsupported key " + part;
            }

            keys.Add(key);
        }

        if (PresentationSource.FromVisual(target) is not HwndSource source)
        {
            return "unsupported no source";
        }

        if (!IsWindowEnabled(source.Handle))
        {
            return "blocked";
        }

        foreach (var key in keys)
        {
            // A handled key-down produces no text, as with real keys: a TextBox inserts Space in its own
            // KeyDown handler, and raising the text as well would type it twice.
            var handled = Raise(target, source, key, Keyboard.PreviewKeyDownEvent) || Raise(target, source, key, Keyboard.KeyDownEvent);
            if (!handled && Character(key) is { } text)
            {
                RaiseText(target, text);
            }

            if (!Raise(target, source, key, Keyboard.PreviewKeyUpEvent))
            {
                Raise(target, source, key, Keyboard.KeyUpEvent);
            }
        }

        return "ok";
    }

    /// <summary>
    /// The element behind an automation peer, or for a window the element that has logical focus in
    /// it (the window itself when nothing has). A window's peer may be missing, because its runtime id
    /// comes from its HWND provider; then the window is found by <paramref name="windowHandle"/>.
    /// </summary>
    public static UIElement? Target(AutomationPeer? peer, nint windowHandle)
    {
        switch (peer)
        {
            case UIElementAutomationPeer { Owner: var owner }:
                return owner is Window window ? FocusedIn(window) : owner;
            case ItemAutomationPeer { Item: UIElement item }:
                return item;
            case ItemAutomationPeer { ItemsControlAutomationPeer.Owner: ItemsControl items } itemPeer:
                return items.ItemContainerGenerator.ContainerFromItem(itemPeer.Item) as UIElement;
        }

        if (windowHandle != 0 && HwndSource.FromHwnd(windowHandle)?.RootVisual is Window byHandle
            && Application.Current?.Windows.OfType<Window>().Contains(byHandle) == true)
        {
            return FocusedIn(byHandle);
        }

        return null;
    }

    private static UIElement FocusedIn(Window window) => FocusManager.GetFocusedElement(window) as UIElement ?? window;

    /// <summary>Raises one key event; true when a handler marked it handled.</summary>
    private static bool Raise(UIElement target, PresentationSource source, Key key, RoutedEvent routedEvent)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key) { RoutedEvent = routedEvent };
        target.RaiseEvent(args);
        return args.Handled;
    }

    private static void RaiseText(UIElement target, string text)
    {
        var composition = new TextComposition(InputManager.Current, target, text);
        var preview = new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled)
        {
            target.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition) { RoutedEvent = TextCompositionManager.TextInputEvent });
        }
    }

    /// <summary>The text a key types without modifiers, independent of the keyboard layout: lower-case letters, digits, space.</summary>
    private static string? Character(Key key) => key switch
    {
        >= Key.A and <= Key.Z => ((char)('a' + (key - Key.A))).ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        Key.Space => " ",
        _ => null,
    };

    [DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(nint hwnd);
}
