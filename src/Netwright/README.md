# Netwright

Playwright-style automation for .NET desktop apps. Netwright is an MCP server that lets AI agents see and operate WPF, WinForms and WinUI apps: compact snapshots with stable refs, selectors, background actions that don't steal your focus, change reports, expectations, app output and crash reports.

<!-- mcp-name: io.github.ricfanin/netwright -->

## Install

Requires Windows 10/11 and the .NET 10 SDK.

```json
{
  "mcpServers": {
    "netwright": {
      "command": "dnx",
      "args": ["Netwright", "--yes"]
    }
  }
}
```

Or install it as a global tool: `dotnet tool install -g Netwright`, then use `"command": "netwright"`.

## Tools

`desktop_app`, `desktop_snapshot`, `desktop_find`, `desktop_inspect`, `desktop_click`, `desktop_type`, `desktop_set_state`, `desktop_press_key`, `desktop_scroll`, `desktop_window`, `desktop_screenshot`, `desktop_wait`, `desktop_expect`, `desktop_logs`.

Documentation, benchmark and source: https://github.com/ricfanin/netwright
