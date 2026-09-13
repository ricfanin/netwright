# Netwright Integration Guide

How to connect Netwright to MCP clients, and how agents get the best results from it.

- [Requirements](#requirements)
- [Client configuration](#client-configuration)
- [Server options](#server-options)
- [Working with the tools](#working-with-the-tools)
- [Making your app automation-friendly](#making-your-app-automation-friendly)
- [From an agent session to a regression test](#from-an-agent-session-to-a-regression-test)
- [Troubleshooting](#troubleshooting)
- [Security](#security)

## Requirements

- Windows 10 or 11.
- The .NET 10 SDK, for `dnx` or `dotnet tool`. Netwright ships self-contained, so your apps can target any .NET or .NET Framework version.
- The Target App must run in the same interactive desktop session as the server, at the same or lower integrity level. A non-elevated server cannot automate an elevated app.

## Client configuration

### Claude Code

```bash
claude mcp add netwright -- dnx Netwright --yes
```

or in `.mcp.json`:

```json
{
  "mcpServers": {
    "netwright": { "command": "dnx", "args": ["Netwright", "--yes"] }
  }
}
```

### Claude Desktop, Cursor, VS Code (GitHub Copilot) and other clients

Every stdio MCP client uses the same shape:

```json
{
  "mcpServers": {
    "netwright": {
      "command": "dnx",
      "args": ["Netwright", "--yes", "--", "--allow", "MyApp*"]
    }
  }
}
```

VS Code uses `"servers"` instead of `"mcpServers"` in `.vscode/mcp.json`.

### Global tool instead of dnx

```bash
dotnet tool install -g Netwright
```

```json
{ "mcpServers": { "netwright": { "command": "netwright" } } }
```

### From source

```json
{
  "mcpServers": {
    "netwright": { "command": "dotnet", "args": ["run", "--project", "C:/src/netwright/src/Netwright", "-c", "Release"] }
  }
}
```

## Server options

| Option | Default | Purpose |
|---|---|---|
| `--allow <pattern>` (repeatable), or `NETWRIGHT_ALLOW="a*;b.exe"` | allow all | Restrict which executables can be launched or attached. The pattern matches the full path, file name, or name without `.exe`; `*` and `?` are wildcards |
| `--action-timeout <ms>` | 5000 | How long actions wait for their target to be Actionable |
| `--settle-timeout <ms>` | 3000 | Upper bound of the wait for the UI to settle after an action |
| `--max-snapshot-lines <n>` | 300 | Line budget of a filtered Snapshot |
| `NETWRIGHT_DOTNET` | `dotnet` | SDK host used to build projects for `desktop_app action=launch project=…` |

## Working with the tools

The server sends these rules to the client as MCP instructions; they are repeated here for humans.

1. **Start the app from its project** while developing: `desktop_app action=launch project=src/MyApp/MyApp.csproj`. It builds and starts the real executable, and build errors come back compactly.
2. **Take one Snapshot, then act.** Refs such as `e12` stay valid while the element exists. For elements you know, use Selectors like `#btnSave` and skip the Snapshot.
3. **Read the Change Report.** Each action returns what appeared, disappeared or changed once the UI settled, so a dialog that opens or a validation message that appears is already in the response.
4. **Wait for asynchronous work** with `desktop_wait text="Loaded"` or `desktop_wait target=#btnNext state=enabled`. Don't poll with Snapshots.
5. **Verify with `desktop_expect`.** It is cheap, retries until timeout, and the check becomes an assertion in the exported test.
6. **Stay in the background.** Use `desktop_type` and `desktop_set_state` rather than key presses. Pass `foreground=true` only when a tool asks for it (`NEEDS_FOREGROUND`).
7. **Check `desktop_logs`** when behaviour is surprising. Crashes are reported automatically in the next response.

Example session:

```
> desktop_app action=launch project=src/Orders/Orders.csproj
Launched Orders.exe (pid 9120, WPF, launched by Netwright)
- window "Orders" [e1]

> desktop_type target=#txtCustomer text="Rossi"
set text of textbox "Customer" [e4] #txtCustomer
~ textbox "Customer" [e4] #txtCustomer value="Rossi"
~ button "Save" [e9] #btnSave

> desktop_click target=#btnSave
clicked button "Save" [e9] #btnSave
+ text "Order 42 saved" #lblStatus

> desktop_expect target=#lblStatus assertion=text_contains expected=saved
pass: text "Order 42 saved" #lblStatus containing "saved"
```

## Making your app automation-friendly

Netwright works with standard controls out of the box. A few habits make agents and exported tests much more reliable:

- **Set AutomationIds** on anything interactive or checked by tests: `AutomationProperties.AutomationId` in WPF/WinUI/Avalonia, `Name` in WinForms. Selectors and exported tests prefer them over visible text, which changes with localization.
- **Name inputs.** Use `AutomationProperties.Name` or `AutomationProperties.LabeledBy` in WPF, and `AccessibleName` in WinForms. Otherwise a text box shows up as `textbox [e7]` with no label.
- **Custom controls** need an `AutomationPeer` (WPF, WinUI, Avalonia) or an `AccessibleObject` (WinForms). Without one their content is invisible to UI Automation. Expose Invoke, Value or Toggle patterns so the agent can operate them in the background.
- **Keep virtualization on.** Netwright handles virtualized lists and grids. Non-virtualized grids with thousands of rows are slow for every UI Automation client.
- **Write diagnostics to `Trace`.** `Trace.WriteLine` output reaches `desktop_logs` even when the app was attached rather than launched.

## From an agent session to a regression test

Every successful action and expectation is recorded with a stable Selector. After an agent has verified a flow, ask it to call `desktop_export_test`. It returns a C# test (xUnit, NUnit or MSTest) that uses [Netwright.Testing](../src/Netwright.Testing/README.md) and replays the same steps without an agent.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| The client does not list `desktop_*` tools | Run `dnx Netwright --yes -- --version` in a terminal to confirm the SDK can start it. Check the client's MCP log |
| `NOT_ALLOWED` | The server was started with `--allow`; add a pattern for the app |
| `APP_NOT_RESPONDING` | The app's UI thread is busy or hung; UI Automation calls time out. Wait, or inspect with `desktop_logs` |
| Attach finds the process but the Snapshot is empty | The app runs elevated. Start the MCP client elevated or run the app normally |
| `NEEDS_FOREGROUND` on a click | The element has no click pattern (custom-drawn control); retry with `foreground=true`, or give it an AutomationPeer |
| No `debug` lines in `desktop_logs` | A debugger (Visual Studio) or DebugView is attached and receives OutputDebugString first |
| `desktop_type` sets the text but the view model isn't updated | WPF bindings with `UpdateSourceTrigger=LostFocus` only update when focus moves. Use `foreground=true`, or `UpdateSourceTrigger=PropertyChanged` |
| Snapshot of a huge grid is slow | Non-virtualized controls expose every row. Enable virtualization, or target rows with Selectors |

## Security

- Netwright only talks to the local machine over stdio; it opens no network ports.
- Use `--allow` so an agent cannot operate unrelated apps, such as your mail client or password manager, on the same desktop.
- Snapshots, values and screenshots are sent to the model. Password fields are masked as `value=***`, but screenshots show whatever is on screen.
- `desktop_app`, `desktop_click`, `desktop_press_key` and `desktop_window` are annotated as destructive, and read-only tools as read-only, so clients can ask for confirmation selectively.
