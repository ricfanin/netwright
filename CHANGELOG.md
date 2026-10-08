# Changelog

## 2.1.0

Netwright now tests your WPF and WinForms app in the background for real. It never takes your focus, and by default the app never even appears on screen.

### Added

- **Hidden apps.** Apps launched by Netwright or Netwright.Testing run hidden by default: their windows never appear on screen or in the taskbar. Snapshots, actions and screenshots keep working. A Foreground Action shows the window only while it runs. Pass `visible: true` to `desktop_app` (or `Visible = true` to `LaunchRequest`) to watch the app. ([ADR 0008](docs/adr/0008-launched-apps-run-hidden.md))
- **Netwright Companion.** This small assembly is loaded through a .NET startup hook into apps Netwright launches (.NET 8 or later). It performs Background actions inside the app. ([ADR 0007](docs/adr/0007-in-process-companion-via-startup-hook.md))
- **Bindings are committed.** Text set in the background now updates bindings that wait for focus to leave the field (WPF `LostFocus`, the default for `TextBox.Text`, and WinForms `OnValidation`), so your view model sees the value as it would after a real edit.

### Fixed

- **Background actions no longer bring the app to the front.** Typing, checking, clicking, selecting and expanding through cross-process UI Automation let the app take the foreground. They now run inside the app. For attached apps, .NET Framework apps and WinUI apps, which have no Companion, Netwright gives the foreground back to your window and says so in the result.
- **Selecting an item in a closed WPF combo box no longer opens its drop-down.**

### Notes

- Tests exported to Netwright.Testing run hidden too, so a test run no longer covers your screen.
- Attached apps, .NET Framework apps and apps on .NET before 8 do not load the Companion. They stay visible and keep the previous behaviour.

## 2.0.0

WPF-MCP becomes Netwright: a rewrite with compact Snapshots and stable Refs, Selectors, Background actions, Change Reports, app output and crash reports, Test Export and the Netwright.Testing library. See [Migrating from WPF-MCP 1.x](README.md#migrating-from-wpf-mcp-1x).
