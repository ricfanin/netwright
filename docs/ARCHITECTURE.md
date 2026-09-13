# Netwright Architecture

Netwright lets an AI Agent see and operate .NET desktop apps through Windows UI Automation (UIA). The vocabulary used here (Target App, Snapshot, Ref, Selector, Actionable, Settled, Change Report, Foreground Action…) is defined in [`CONTEXT.md`](../CONTEXT.md). The reasons behind the big decisions are recorded in [`docs/adr`](adr).

```
 Agent (Claude Code, Copilot, Cursor…)
        │  MCP over stdio
 ┌──────▼───────────────────────────────┐
 │ Netwright (MCP server)               │  14 desktop_* tools: argument parsing,
 │  DesktopTools                        │  compact text rendering, isError results
 └──────┬───────────────────────────────┘
        │  in-process API
 ┌──────▼───────────────────────────────┐
 │ Netwright.Engine                     │
 │  DesktopSession ── TargetApp         │  lifecycle, allow list, crash reports
 │   ├─ UiaTreeReader  (capture)        │  one cached UIA query per window
 │   ├─ SnapshotRenderer / Refs         │  filtered text, stable Refs
 │   ├─ SelectorParser / Matcher        │  #id, role "name", a >> b
 │   ├─ ChangeReporter                  │  +/-/~ diff by RuntimeId
 │   ├─ PatternCalls / ForegroundScope  │  background patterns, real input
 │   └─ AppOutput / DebugOutputCapture  │  stdout, stderr, OutputDebugString
 └──────┬───────────────────────────────┘
        │  COM (UIAutomationCore), Win32
 ┌──────▼───────────────────────────────┐
 │ Target App (WPF, WinForms, WinUI …)  │  AutomationPeers / MSAA bridge
 └──────────────────────────────────────┘
```

The Engine has no MCP dependency. The same Engine powers the testing library used by Test Exports (ADR 0004). It talks to UI Automation through the `Interop.UIAutomationClient` COM interop assembly and simulates input with `SendInput`. It has no FlaUI dependency (ADR 0006), so every assembly targets plain `net8.0`/`net10.0` and can ship as a dotnet tool.

## Capturing the UI

Crossing the process boundary is the cost that matters, so the Engine never walks the tree element by element:

1. **Find windows with Win32.** `EnumWindows` lists the visible top-level windows of the Target App's processes. This covers child processes too, found through a Toolhelp snapshot of parent PIDs. Owned windows (dialogs, tool windows) are skipped because UIA shows them inside their owner. This replaced a UIA desktop search that cost ~90 ms per capture.
2. **One cached query per window.** `ElementFromHandleBuildCache` with `TreeScope_Subtree` fetches the whole control-view subtree and the properties the Engine needs in a single call.
3. **Few properties, inferred patterns.** Each cached property costs one provider call per element. Pattern availability is inferred from the pattern's own state property (read with `GetCachedPropertyValueEx(ignoreDefault)`), so the full capture needs 20 properties instead of ~40. Bounds, class name and framework are read live only for the element that needs them.
4. **Offscreen elements filtered in the provider** (ADR 0005). A capture that misses a target retries once with offscreen elements included.
5. **Light captures for polling.** Settle and wait loops use 12 properties and `AutomationElementMode_None`, which returns no live references and costs about half as much.

Measured on the Fixture Apps (median):

| Screen | Before optimisation | Now |
|---|---:|---:|
| WPF form (50 elements), full capture | 160 ms | 87 ms |
| WPF form, light capture | – | 43 ms |
| WinForms grid, 1000 non-virtualized rows | 8.2 s | 1.7 s |

The numbers come from `tests/Netwright.IntegrationTests/CapturePerformanceProbe.cs`, run with `--filter Category=Perf`.

## From tree to text

`UiTree` holds plain `UiNode`s: role, name, AutomationId, states, inferred patterns, RuntimeId key, and a live element reference used only to act. Everything after capture is pure and unit-tested.

- **Refs** (`RefRegistry`) are keyed by RuntimeId, so the same element keeps the same Ref across Snapshots. Virtualized lists recycle containers under the same RuntimeId; an item whose name changed counts as a different element and its old Ref is stale.
- **SnapshotRenderer** keeps interactive elements, text and named containers. It collapses unnamed panes, drops scrollbars, title bars and the parts of composite controls, turns grid rows into `row "a | b | c"` for both WPF (DataItem rows) and WinForms (Custom rows with DataItem cells), and caps items and lines.
- **Selectors** are parsed into steps and matched in memory against the captured tree. Snapshot and Selector therefore always agree on what exists. `SelectorBuilder` produces the shortest unique Selector for an element (AutomationId first); it is shown by `desktop_inspect` and recorded for Test Export.
- **ChangeReporter** compares two trees by RuntimeId, restricted to visible, reportable elements. It emits the topmost added subtrees, removed elements, changed states and focus moves, within a line budget.

## Acting

Every action runs through one pipeline in `DesktopSession.Actions`:

```
Locate ──► Perform ──► Settle ──► Change Report
```

- **Locate** captures repeatedly until the target exists and is Actionable (enabled, on screen; offscreen targets get `ScrollIntoView` once), or fails with `ELEMENT_NOT_FOUND`/`NOT_ACTIONABLE` after the action timeout. The capture it ends with is the "before" state.
- **Perform** runs on a worker thread. UIA calls that open modal UI do not return until the dialog closes: WPF defers the click, but WinForms runs the handler inside the Invoke call. After `BlockingCallTimeoutMs` (1.5 s) the pipeline carries on and says so in a note.
  - Background input uses patterns only (Invoke, Toggle, SelectionItem, ExpandCollapse, Value, RangeValue, Scroll, Window, and LegacyIAccessible's default action).
  - Foreground Actions wrap the input in `ForegroundScope`. It records the foreground window and cursor, activates the Target App window (temporarily attaching to the foreground thread's input queue, the only reliable way Windows allows it), and restores both afterwards. Focus is left on the app if the action opened a new window or menu.
- **Settle** polls light captures until two consecutive fingerprints match after a 150 ms quiet period (or 3 s pass), then takes one full capture as the "after" state (ADR 0002).

All session operations are serialized with a semaphore: a Snapshot must never observe half of another action.

## Diagnostics

- **stdout/stderr** are redirected when Netwright launches the app. You cannot attach to the output of a process that is already running.
- **OutputDebugString** (which `Trace.WriteLine` uses) is read from the session-wide `DBWIN_BUFFER` shared memory, as Sysinternals DebugView does, and filtered by the Target App's PIDs. It needs no privileges; it receives nothing while a debugger is attached to the app.
- **Crash Reports.** When the process exits without being asked to, the Engine looks up event 1026 from the ".NET Runtime" provider in the Application event log. That event holds the exception type, message and stack for both .NET Framework and .NET 5+. If it is missing, the Engine falls back to the stderr tail. The report is queued and prepended to the next tool response.

## Projects

| Project | Target | Role |
|---|---|---|
| `src/Netwright.Engine` | net8.0; net10.0 (Windows-only) | Automation engine |
| `src/Netwright` | net10.0, self-contained per RID (win-x64, win-arm64), ReadyToRun | MCP server, `dotnet tool` + NuGet `McpServer` package |
| `src/Netwright.Testing` | net8.0; net10.0 (Windows-only) | Library that exported tests run on |
| `tests/Netwright.Testing.Tests` | net10.0-windows | Test Export generation, including compiling the generated code with Roslyn |
| `tests/Netwright.Engine.Tests` | net10.0-windows | Pure unit tests (selectors, rendering, diffs, parsers) |
| `tests/Netwright.IntegrationTests` | net10.0-windows | Engine and MCP server against the Fixture Apps |
| `tests/fixtures/*` | WPF net8, WinForms net8 + net48 | Fixture Apps implementing [FIXTURE-CONTRACT.md](../tests/fixtures/FIXTURE-CONTRACT.md) |
| `benchmarks/Netwright.Benchmarks` | net10.0-windows | Token and latency benchmark against any MCP server |
