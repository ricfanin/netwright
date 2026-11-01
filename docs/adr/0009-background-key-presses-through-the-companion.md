---
status: accepted (amends ADR 0007)
---

# Press keys in the Background by raising WPF key events through the Companion

ADR 0007 left key presses as Foreground Actions: they go through `SendInput`, which Windows delivers only to the focused window. In user acceptance runs against a Hidden App, every arrow key in a tree, Enter in a filter row and Escape on a popup therefore showed the app and took the User's focus. ADR 0007 named the Companion as the channel through which key presses could move to the Background. This ADR records how, and what that costs.

## The trade-off

Raised WPF key events are not real input. Handlers on the target element and its ancestors see them. Anything WPF does at the `InputManager` level does not happen: `KeyboardNavigation`, access keys, `Keyboard.Modifiers`, IME. A third-party control that reads the real keyboard state does not see them either. A step can therefore pass in Netwright and behave differently by hand, without any error. Because this turns `press_key` without `foreground` from "always `NEEDS_FOREGROUND`" into the path Agents take by default, the divergence is always visible: every Background key result says `(raised as WPF key events)`.

## What is guaranteed

For each key, on the target element:

1. `PreviewKeyDown` tunnels from the window down to the element, then `KeyDown` bubbles back up, unless a preview handler marked the key handled.
2. For a character key whose key-down nobody handled, `PreviewTextInput`, then `TextInput` unless handled. A handled key-down produces no text, as with real keys. A `TextBox` inserts Space in its own `KeyDown` handler, for example.
3. `PreviewKeyUp`, then `KeyUp` unless handled.

Class handlers run as for any routed event. So input bindings fire: `CommandManager` checks `KeyBinding`s in the class handler for `KeyDown`, on every element the event bubbles through, the window included. Verified with a window-level `KeyBinding` for F5 in the WPF Fixture App (`BackgroundKeyTests.A_key_binding_without_modifiers_runs_its_command`).

Nothing else is guaranteed. Nothing at the `InputManager` level runs: no Tab navigation, no access keys, no IME. A key gesture still reads the modifiers from the real keyboard, so a binding without modifiers does not match while the User happens to hold Ctrl.

## Delivery mechanism

`desktop_press_key` with `foreground: false` asks the Companion to press the keys on the target element. The Engine sends the `keys` request with virtual-key codes. The Companion finds the element the same way it does for patterns, the WPF peer by RuntimeId, and raises the events on its dispatcher. A window's RuntimeId comes from its HWND provider, so a window is found by its handle, and the keys go to the element with logical focus in it (`FocusManager.GetFocusedElement`), or else the window itself.

Without a target, the keys go where a Foreground press without a target goes: the focused element, or else the main window.

If the target's window is disabled, because a modal dialog is open over it, the Companion raises nothing. Real keys could not reach it either. The Engine returns `NOT_ACTIONABLE` and points to the dialog.

## Modifiers

WPF reads modifier state (`Keyboard.Modifiers`) from the real keyboard, not from the event, so a raised Ctrl+S looks like a plain S to every handler. Chords with Ctrl, Alt, Shift or Win, and the modifier keys alone, are therefore refused in the Background with `NEEDS_FOREGROUND` before anything is raised. They are never sent half-faked.

## Text and IME

A character key raises `TextInput` with the text that key types without modifiers: a lower-case letter, a digit or a space. That text does not depend on the keyboard layout, and there is no IME composition. Upper case, symbols, dead keys and IME input need Shift, AltGr or a composition, so they are out of scope. Entering text stays the job of `desktop_type`, which sets the Value in the Background and commits bindings (ADR 0007).

## Accelerators

- **Without modifiers** (F5, Delete, Enter, Escape as `KeyBinding`s, and command bindings): they fire, see "What is guaranteed".
- **With modifiers** (Ctrl+S, Alt+F4): these are chords, so they get `NEEDS_FOREGROUND`.
- **Access keys and menu mnemonics** (Alt+letter, the letter while a menu shows its access keys): `AccessKeyManager` runs in `InputManager` post-processing, not on routed events. They need the Foreground. A dialog's `IsDefault` and `IsCancel` buttons register Enter and Escape as access keys, so those presses should not reach them in the Background either. That case is not covered by a test, because the Fixture App's dialog has no such buttons.
- **Tab focus navigation** does not happen, because `KeyboardNavigation` does not run.

## Fallback policy

There is no silent fallback to the real keyboard. A Background press that cannot be delivered returns `NEEDS_FOREGROUND` with the reason:

- a chord with a modifier;
- an app without a WPF Companion: WinForms, WinUI, attached apps, .NET Framework, .NET before 8;
- an element the Companion cannot find.

The Agent decides whether to retry with `foreground: true`. Falling back on its own would take the User's focus, which `foreground: false` promises not to do.

The `foreground` default stays `false`, as for every action. Before this ADR, a press without `foreground: true` always returned `NEEDS_FOREGROUND`. Now it runs in the Background where it can.

## Consequences

- Single keys without modifiers no longer take the User's focus in WPF apps launched by Netwright (.NET 8 or later).
- Every Background key result ends with `(raised as WPF key events)`. Whoever reads it, an Agent or a person reading an exported run, knows it was not real input. When the app does not react as it does to real keys, the Agent retries with `foreground: true`.
- Test Export records the mode. A press raised as WPF key events exports as `PressKeyAsync(keys, target, foreground: false)` and replays the same way. A Foreground press exports as before and replays with real input. An exported test does what the Agent actually did.
- The Companion's fixed request list from ADR 0007 gains `keys`. It still runs no arbitrary code and swallows its own failures.
- WinForms is out of scope. Posting `WM_KEYDOWN`/`WM_CHAR` to a control's handle diverges from real input as well, at different edges, and needs its own ADR if real use asks for it. WinUI has no Companion yet.
