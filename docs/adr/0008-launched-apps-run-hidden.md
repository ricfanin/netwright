---
status: accepted
---

# Apps launched by Netwright run hidden by default

With ADR 0007, Background actions no longer take the foreground. A launched app still appeared on screen, though, on top of the User's windows. The User asked for it not to appear at all, the way a headless browser works.

A Win32 UI app cannot run without a desktop, and UI Automation only works on the same desktop as the client. A separate desktop or an off-screen position would break either UI Automation or the Engine's offscreen filtering (ADR 0005). Instead, the Companion hides every top-level window of the UI thread just before it is first shown. A `WH_CALLWNDPROC` hook watches `WM_SHOWWINDOW` and `WM_WINDOWPOSCHANGING` with `SWP_SHOWWINDOW`. The window keeps its size and position, so UI Automation reads it as on-screen and `PrintWindow` still renders it for screenshots. Two hiding methods are needed, measured on the Fixture Apps on 2026-10-08:

- **Layered, zero opacity, click-through, no taskbar button.** This works for WinForms, whose controls are child windows that UI Automation still walks. WPF windows refuse `SetLayeredWindowAttributes` (error 87).
- **DWM cloak.** This works for WPF, whose controls are not windows. UI Automation leaves cloaked windows out of the tree, so the Engine captures cloaked owned windows (dialogs) directly instead of expecting them inside their owner. Cloaking hides WinForms controls from UI Automation, so it is used only when the layered method fails.

## Consequences

- `LaunchRequest.Visible` (`visible` on `desktop_app`) shows the app. Hiding needs the Companion, so apps launched without one are always visible: .NET Framework, .NET before 8, and apps whose UI is created on a thread other than the main one.
- Netwright.Testing tests run hidden by default, so a test run no longer covers the screen with windows.
- A Foreground Action makes the window visible only while it runs, so real mouse and keyboard input reaches a window the User can see, then hides it again. In a hidden app focus is never left on the app after a Foreground Action, even when a menu or dialog opened, because the User's keystrokes would go to an invisible window.
- An app that activates itself on purpose can still take keyboard focus while invisible. Netwright does not block activation, because Foreground Actions need it.
