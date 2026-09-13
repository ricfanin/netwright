# Fixture App Contract

Every Fixture App (WPF, WinForms, WinUI) implements the same screens with the same AutomationIds and texts. That way integration tests and the Benchmark can run unchanged against any of them.

Rules:
- In WinForms, the AutomationId is `Control.Name`.
- In WPF and WinUI, set `AutomationProperties.AutomationId` explicitly, and set `AutomationProperties.Name` wherever a label is not the element's own content.
- Items marked **WPF/WinUI only** can be skipped in WinForms.

## Main window

- **Title:** `Netwright Fixture (WPF)`, `Netwright Fixture (WinForms)`, or `Netwright Fixture (WinUI)`.
- **Size:** 1000×750.
- **Startup:** the window starts centered and activated.
- **Command-line arguments:**
  - `--tab <Form|Async|Grid|Tree|Keyboard|Scroll>` selects the initial tab.
  - `--no-activate` avoids stealing focus at startup where the framework allows it.

### Top bar (always visible, above the tabs)

| AutomationId | Control | Text / Name | Behavior |
|---|---|---|---|
| `menuMain` | Menu | — | Menu **File** (`miFile`) containing **Open modal...** (`miOpenModal`) and **Exit** (`miExit`) |
| `btnOpenModal` | Button | `Open modal` | Opens a modal dialog, titled `Confirm`, with text `Are you sure?` (`lblConfirm`) and two buttons: `btnYes` "Yes" and `btnNo` "No". Clicking either closes the dialog and sets `lblModalResult` to `Modal result: Yes` or `Modal result: No` |
| `lblModalResult` | Label/TextBlock | `Modal result: none` | |
| `btnOpenWindow` | Button | `Open tool window` | Opens a non-modal window, titled `Tool Window`, containing a TextBox `txtToolNote` and a Button `btnToolClose` "Close" |
| `btnTrace` | Button | `Write diagnostics` | Writes `fixture-trace: hello` via `System.Diagnostics.Trace.WriteLine`, `fixture-stdout: hello` via `Console.Out.WriteLine` (then flushes), and `fixture-stderr: hello` via `Console.Error.WriteLine`. Then sets `lblDiag` to `Diagnostics written` |
| `lblDiag` | Label | `` (empty) | |
| `btnCrash` | Button | `Crash` | Throws `new InvalidOperationException("Fixture crash requested")` on the UI thread, **unhandled** (the process must terminate) |

### Tabs: TabControl `tabs`

**Tab "Form"** (`tabForm`)

| AutomationId | Control | Text / Name | Behavior |
|---|---|---|---|
| `txtName` | TextBox | Name `Name` | |
| `txtEmail` | TextBox | Name `Email` | |
| `cmbCountry` | ComboBox (non-editable) | Name `Country` | Items: Italy, Germany, France, Spain, United States. No initial selection |
| `chkTerms` | CheckBox | `Accept terms` | |
| `rbPlanFree` | RadioButton | `Free` | Checked initially |
| `rbPlanPro` | RadioButton | `Pro` | |
| `sldQuantity` | Slider / TrackBar | Name `Quantity` | Range 0–10, initial value 1 |
| `btnSubmit` | Button | `Submit` | **Disabled until `chkTerms` is checked.** Sets `lblResult` |
| `btnReset` | Button | `Reset` | Clears all fields, unchecks terms, selects Free, sets quantity to 1, and sets `lblResult` to `` |
| `lblResult` | Label | `` | After Submit: `Submitted: {name} <{email}> {country} {Free\|Pro} x{quantity}` |

**Tab "Async"** (`tabAsync`)

| AutomationId | Control | Text / Name | Behavior |
|---|---|---|---|
| `btnLoad` | Button | `Load data` | Disables itself and sets `lblStatus` to `Loading...`. After **1500 ms** (async, UI thread stays responsive) it fills `lstItems` with Alpha, Beta, Gamma, sets `lblStatus` to `Loaded 3 items`, and re-enables itself |
| `lblStatus` | Label | `Idle` | |
| `lstItems` | ListBox | Name `Items` | Empty initially |
| `btnEnableLater` | Button | `Enable later` | After **1000 ms**, enables `btnLater` |
| `btnLater` | Button | `Later` | Disabled initially. When clicked, sets `lblLater` to `Later clicked` |
| `lblLater` | Label | `` | |

**Tab "Grid"** (`tabGrid`)

| AutomationId | Control | Text / Name | Behavior |
|---|---|---|---|
| `gridOrders` | DataGrid / DataGridView | Name `Orders` | 1000 rows, read-only. Columns: `Id` (1..1000), `Customer` (`Customer 1`...), `Amount` (`Id * 10` formatted as `0.00` with invariant culture). Virtualization stays ON (the default) |

**Tab "Tree"** (`tabTree`)

| AutomationId | Control | Text / Name | Behavior |
|---|---|---|---|
| `treeFolders` | TreeView | Name `Folders` | `Root` › (`Documents` › (`Invoices`, `Reports`), `Pictures`). Everything collapsed initially |
| `lblSelectedNode` | Label | `` | On selection: `Selected: {node text}` |
| `expAdvanced` | Expander | `Advanced` | **WPF/WinUI only.** Collapsed initially and contains CheckBox `chkVerbose` "Verbose logging" |

**Tab "Keyboard"** (`tabKeyboard`)

| AutomationId | Control | Text / Name | Behavior |
|---|---|---|---|
| `txtKeys` | TextBox | Name `Keys` | On KeyDown, sets `lblLastKey` to `Last key: {Key}`. Use the framework key name, e.g. `Enter` in WPF or `Return` in WinForms; tests accept both |
| `lblLastKey` | Label | `Last key: none` | |
| `pnlMouse` | Border/Panel, 200×100, visible background | Name `Mouse target` | **Has no Invoke pattern.** Left click sets `lblMouse` to `Mouse: left`, double click to `Mouse: double`, right click to `Mouse: right` |
| `lblMouse` | Label | `Mouse: none` | |

**Tab "Scroll"** (`tabScroll`)

| AutomationId | Control | Text / Name | Behavior |
|---|---|---|---|
| `scrLong` | ScrollViewer / AutoScroll Panel | Name `Long list` | Contains 100 buttons stacked vertically, `btnItem001`..`btnItem100`, with text `Item 1`..`Item 100`. Clicking one sets `lblScrollClicked` to `Clicked Item N` |
| `lblScrollClicked` | Label | `` | Placed outside the scroll area |

## Known differences between technologies

These are how the frameworks surface through UI Automation, not bugs in the fixtures. Tests must tolerate them.

| Topic | WPF | WinForms (.NET 8 / .NET Framework 4.8) |
|---|---|---|
| Menu items | `miFile`, `miOpenModal`, `miExit` AutomationIds are exposed | Menu items have **no AutomationId**. Target them by name (`menuitem "File"`) |
| Tab items | TabItem has the AutomationId | .NET 8: both the TabItem and the page Pane have `tabForm`. .NET Framework: only the Pane has it. Target tabs by `tab "Form"` |
| Slider | RangeValue pattern, 0–10 | TrackBar exposes only Value, as a percentage string |
| Grid | `grid` (DataGrid) with virtualized `row` items | .NET 8: `grid`. .NET Framework: `table`. All 1000 rows are exposed (no virtualization); row names are localized ("Row 0") |
| ComboBox | Items appear after Expand | Items appear after Expand; Value gives the selected text |
| Owned windows | Dialog and tool window appear inside the main window in the UIA tree | Same |
| Caption labels | `Name:` / `Email:` text next to the fields | `Name` / `Email` text labels share the input's name; filter by role |
| Modal and crash buttons | Deferred by the WPF dispatcher | Posted with `BeginInvoke`, because a UIA Invoke runs WinForms click handlers synchronously and would block on `ShowDialog` |

### WinUI 3 Fixture App (Tier 2)

| Topic | WinUI 3 |
|---|---|
| Confirm dialog | A separate `Window` titled `Confirm`, owned by the main window, which is disabled while it is open. It is not a `ContentDialog`. Opened through the dispatcher |
| Tabs | `tabs` is a TabView; the tab items live one level deeper, under an inner list `TabListView` |
| Menu | `menuMain` is a `menubar`. Drop-down items keep their AutomationIds inside a popup window |
| Grid | There is no DataGrid: `gridOrders` is a `list` of `item`s named `1 \| Customer 1 \| 10.00` (virtualized, no `rows=`) |
| Tree | Items are direct children of `treeFolders`; nesting only shows through expand/collapse state |
| Expander | `expAdvanced` is exposed as a `button` with ExpandCollapse |
| Crash | Ends with a fail-fast (`0xC000027B`) without a .NET runtime event, so the Crash Report only shows the exit code |

The build is self-contained (no Windows App Runtime install): `bin\Release\net8.0-windows10.0.19041.0\win-x64\Netwright.Fixtures.WinUI.exe`.

## Non-goals

- No timers or animations that keep changing the UI when idle. The UI must become Settled.
- No network or file access.
