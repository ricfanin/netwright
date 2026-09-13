---
status: accepted (supersedes ADR 0003)
---

# Talk to UI Automation through COM interop directly; drop FlaUI

ADR 0003 kept FlaUI.UIA3. Two facts changed the balance:

- **Packaging.** The server must ship as a `dotnet tool` / NuGet `McpServer` package so it can run with `dnx`. The SDK refuses to pack a tool that targets a platform-specific TFM such as `net10.0-windows` (NETSDK1146).
- **FlaUI's dependency.** FlaUI.Core only ships `-windows` assets, which require the WindowsForms shared framework.
- **What the Engine actually used FlaUI for.** Everything else already went through the native `IUIAutomation` interfaces for speed. FlaUI was only creating the `CUIAutomation8` object and simulating mouse and keyboard input.

The Engine now references `Interop.UIAutomationClient`, whose `netstandard2.0` assets load on plain `net8.0`/`net10.0`. It sends input through a small `SendInput` wrapper, and its assemblies are marked `[SupportedOSPlatform("windows")]`.

Native AOT is still not pursued. Built-in COM interop remains a blocker, and ADR 0003's reasoning about where the time goes still holds.
