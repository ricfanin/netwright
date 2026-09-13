# Netwright

**Playwright-style automation for .NET desktop apps.** Netwright is an MCP server that lets AI agents see and operate WPF, WinForms and WinUI apps: they launch your project, read the UI as compact text, act without stealing your mouse, verify the result, and turn the verified flow into a regression test.

[![CI](https://github.com/ricfanin/netwright/actions/workflows/ci.yml/badge.svg)](https://github.com/ricfanin/netwright/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Netwright.svg)](https://www.nuget.org/packages/Netwright)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

> Formerly **WPF-MCP**. Version 2.0 is a rewrite; see [Migrating from WPF-MCP 1.x](#migrating-from-wpf-mcp-1x).

```
> desktop_app action=launch project=src/Orders/Orders.csproj
Launched Orders.exe (pid 9120, WPF, launched by Netwright)
- window "Orders" [e1]

> desktop_snapshot
- window "Orders" [e1]
  - textbox "Customer" [e4] #txtCustomer
  - combobox "Country" [e5] #cmbCountry collapsed
  - checkbox "Accept terms" [e6] #chkTerms unchecked
  - button "Save" [e7] #btnSave disabled

> desktop_set_state target=#chkTerms checked=true
checked checkbox "Accept terms" [e6] #chkTerms
~ checkbox "Accept terms" [e6] #chkTerms checked
~ button "Save" [e7] #btnSave

> desktop_click target=#btnSave
clicked button "Save" [e7] #btnSave
+ window "Confirm" [e9] modal
  + text "Save order 42?"
  + button "Yes" [e10]
```

## Why Netwright

- **Made for .NET apps.** Works with WPF, WinForms (.NET and .NET Framework 4.x) and WinUI/MAUI through UI Automation. It launches straight from a `.csproj`, captures stdout/stderr and `Trace` output, and reports crashes with the exception and stack trace.
- **Cheap in tokens.**
  - Snapshots are filtered to what an agent needs: unnamed layout panes are collapsed, and grids are summarized row by row.
  - Actions answer with a **Change Report** of what appeared, disappeared or changed, so the agent rarely needs another snapshot.
  - Refs (`e12`) stay valid across snapshots.
- **Stays out of your way.** Actions run in the **background** through UI Automation patterns, so you keep working while the agent tests. Key presses and right-clicks are explicit, briefly take focus, and give it back.
- **Reliable by default.**
  - Every action waits until its target is enabled and on screen.
  - It then waits for the UI to settle, so async screens don't need sleeps.
  - Failures come back as error codes with a hint about what to do next.
- **From exploration to regression tests.** `desktop_expect` verifies state, and `desktop_export_test` turns the session into a C# test on [Netwright.Testing](src/Netwright.Testing/README.md) (xUnit, NUnit or MSTest).

## Benchmark

Same scenarios, same Fixture App, same token encoding for every server (full methodology and all numbers in [benchmarks/results](benchmarks/results/README.md)):

| WPF Fixture App, median of 5 runs | WPF-MCP 1.0 | [FlaUI-MCP](https://github.com/shanselman/FlaUI-MCP) | [Windows-MCP](https://github.com/CursorTouch/Windows-MCP) | **Netwright 2.0** |
|---|---:|---:|---:|---:|
| Tool definitions, sent every turn (tokens) | 2,023 (24 tools) | 1,418 (12) | 3,984 (20) | **1,387 (15)** |
| Snapshot of a form (tokens) | 1,023 | 785 | 1,956¹ | **385** |
| Snapshot of a 1000-row grid (tokens) | 3,988 | 6,234 | 1,897¹ | **627** |
| Fill and submit a form: calls / tokens | 10 / 2,463 | 10 / 3,469 | –² | **6 / 549** |
| Same form on WinForms: success | 0/5 | 0/5 | –² | **5/5** |
| Wait for an async load: calls / tokens | 5 / 3,337 | 5 / 2,484 | –² | **3 / 405** |
| Screenshot (tokens) | 25,073 | 1,000 | 1,944 | 1,020 |
| WinForms grid with 1000 non-virtualized rows: snapshot time | 9.2 s | 12.9 s | 0.3 s¹ | 1.5 s |

¹ Windows-MCP summarizes the whole desktop instead of the app's UI tree; its grid snapshot does not include the rows. ² Its actions work at screen coordinates with the real mouse, so scripted flows were not run.

Tokens are counted with `o200k_base` as a proxy for Claude's tokenizer. Netwright's actions are slower per call: the form flow takes ~1.8 s against ~0.6 s, because each action waits for the UI to settle. In exchange the response already contains the result, so the agent needs fewer calls and far fewer tokens.

## Install

Requires Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download). Your apps can target any .NET or .NET Framework version.

**Claude Code**

```bash
claude mcp add netwright -- dnx Netwright --yes
```

**Any MCP client** (Claude Desktop, Cursor, VS Code, Copilot…)

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

Prefer a global tool? Run `dotnet tool install -g Netwright` and use `"command": "netwright"`. To restrict which apps an agent may touch, add `"--", "--allow", "MyApp*"` to the args. See the [Integration Guide](docs/INTEGRATION_GUIDE.md) for all options.

## Tools

| Tool | What it does |
|---|---|
| `desktop_app` | Launch an `.exe` or build and launch a project, attach, close, status |
| `desktop_snapshot` | The UI as compact text with Refs |
| `desktop_find` | Elements matching a Selector (`#id`, `role "name"`, `a >> b`) |
| `desktop_inspect` | Properties, patterns and a stable Selector of one element |
| `desktop_click` | Click (background; right/double click with `foreground`) |
| `desktop_type` | Set text (background) or type real keys |
| `desktop_set_state` | Check, select, expand, set a value, or pick an item by name |
| `desktop_press_key` | Key presses and shortcuts (foreground) |
| `desktop_scroll` | Scroll by direction, to an edge, or into view |
| `desktop_window` | List, activate, minimize, maximize, restore, close windows |
| `desktop_screenshot` | Image of a window or element, even when covered |
| `desktop_wait` | Wait for an element state or for text; reports what changed |
| `desktop_expect` | Retrying assertions: text, value, enabled, checked, count… |
| `desktop_logs` | App output: stdout, stderr, debug/trace |
| `desktop_export_test` | Turn the session into a C# regression test |

Full reference: [docs/TOOLS_REFERENCE.md](docs/TOOLS_REFERENCE.md).

## Supported UI technologies

| Tier | Technology | What that means |
|---|---|---|
| 1 | WPF, WinForms (.NET 6+ and .NET Framework 4.x) | A Fixture App, integration tests on every change, benchmarked |
| 2 | WinUI 3, .NET MAUI on Windows | A Fixture App and integration tests |
| 3 | Avalonia, Uno Platform | Best effort through UI Automation |

Other Windows apps (Win32, Qt, Electron…) often work because Netwright speaks plain UI Automation, but they are not supported.

Automation works best when your app sets `AutomationProperties.AutomationId` (WPF/WinUI) or `Name` (WinForms) on the controls you care about. See [Making your app automation-friendly](docs/INTEGRATION_GUIDE.md#making-your-app-automation-friendly).

## Regression tests without an agent

```csharp
[Fact]
public async Task Saving_an_order_asks_for_confirmation()
{
    await using var app = await DesktopApp.LaunchProjectAsync(@"..\..\..\..\src\Orders\Orders.csproj");

    await app.TypeAsync("#txtCustomer", "Rossi");
    await app.CheckAsync("#chkTerms");
    await app.ClickAsync("#btnSave");

    await app.Expect("window \"Confirm\"").ToBeVisibleAsync();
}
```

`dotnet add package Netwright.Testing`. This is the code `desktop_export_test` writes for you.

## How it works

Netwright captures each window with **one cached UI Automation query**. Windows are found with Win32, only about 20 properties are requested, and offscreen elements are filtered inside the provider. From that capture it renders filtered text, resolves Selectors in memory, and diffs trees by RuntimeId to build Change Reports. Details and measurements: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Vocabulary: [CONTEXT.md](CONTEXT.md). Decisions: [docs/adr](docs/adr).

## Migrating from WPF-MCP 1.x

| 1.x | 2.0 |
|---|---|
| `wpf_launch_application`, `wpf_attach_application`, `wpf_close_application` | `desktop_app action=launch\|attach\|close` |
| `wpf_snapshot` (refs reset on every snapshot) | `desktop_snapshot` (refs are stable) |
| `wpf_find_element`, `wpf_get_element_properties` | `desktop_find`, `desktop_inspect` |
| `wpf_click`, `wpf_type` | `desktop_click`, `desktop_type`; both accept a Ref or a Selector |
| `wpf_set_value`, `wpf_toggle`, `wpf_select`, `wpf_expand_collapse` | `desktop_set_state` (`value`, `checked`, `item`, `expanded`) |
| `wpf_press_key` | `desktop_press_key foreground=true` |
| `wpf_scroll`, `wpf_scroll_into_view`, `wpf_focus` | `desktop_scroll` (`direction`, `to`, `into_view`) |
| `wpf_list_windows`, `wpf_switch_window`, `wpf_window_action` | `desktop_window` |
| `wpf_take_screenshot` (base64 text) | `desktop_screenshot` (image content) |
| `wpf_wait_for` | `desktop_wait` (also waits for text) |
| `wpf_console_messages` | `desktop_logs` |
| `wpf_set_background_mode` | Background is the default; pass `foreground=true` per call when needed |
| JSON envelopes (`success`, `data`, `metadata`) | Compact text; errors are `isError` results with `code` and `hint` |

The package is now `Netwright` (command `netwright`), replacing `WpfMcp.Server` (`wpf-mcp`).

## Contributing

Issues and PRs are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md). Build and test with `.\scripts\build.ps1 -Integration`, and measure with `.\scripts\benchmark.ps1`.

## License

MIT
