# Scope: .NET desktop apps, UI Automation first, out-of-process

The project started as a WPF-only MCP server. We broadened it to every .NET desktop UI technology, on both .NET and .NET Framework, and we will rebrand it. Direct competitors either target all of Windows (Windows-MCP, Terminator) or already use the same FlaUI stack (FlaUI-MCP), so being "the .NET one" is the niche where we can win.

The server keeps running out-of-process on UI Automation. An in-process component would see ViewModels and binding errors, and would allow real Background keyboard input in WPF. We deferred it because it would need a separate implementation per UI stack.

Non-.NET apps may work through UI Automation, but they are explicitly Unsupported Apps. We make no guarantees for them, so that we never have to chase Delphi or Electron quirks.

## Consequences

- Support is tiered:
  - Tier 1: WPF and WinForms.
  - Tier 2: WinUI 3, which also covers MAUI on Windows.
  - Tier 3: Avalonia and Uno.
- Uno Platform ships its own App MCP, so Uno stays best effort.
