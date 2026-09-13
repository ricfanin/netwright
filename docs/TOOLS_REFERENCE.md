# Netwright Tools Reference

Netwright exposes 15 MCP tools. Every tool answers with **compact text** meant for the model, plus a small `structuredContent` object (`ok`, `code`, `settled`, `changes`, `elapsedMs`) for scripts. Failures are ordinary tool results with `isError: true`:

```
error NOT_ACTIONABLE: button "Submit" [e17] #btnSubmit is disabled (waited 5000 ms).
hint: Something else must happen first (e.g. fill required fields); check the screen with desktop_snapshot.
```

- [Concepts](#concepts)
- [desktop_app](#desktop_app) · [desktop_snapshot](#desktop_snapshot) · [desktop_find](#desktop_find) · [desktop_inspect](#desktop_inspect)
- [desktop_click](#desktop_click) · [desktop_type](#desktop_type) · [desktop_set_state](#desktop_set_state) · [desktop_press_key](#desktop_press_key) · [desktop_scroll](#desktop_scroll) · [desktop_window](#desktop_window)
- [desktop_screenshot](#desktop_screenshot) · [desktop_wait](#desktop_wait) · [desktop_expect](#desktop_expect) · [desktop_logs](#desktop_logs) · [desktop_export_test](#desktop_export_test)
- [Error codes](#error-codes)

## Concepts

**Snapshot.** The UI of the Target App as an indented list. Each line has a role, a quoted name, a Ref in brackets, the `#AutomationId` when there is one, and attributes:

```
- window "Orders" [e1]
  - textbox "Customer" [e4] #txtCustomer value="Rossi"
  - combobox "Country" [e5] #cmbCountry collapsed
  - checkbox "Accept terms" [e6] #chkTerms unchecked
  - grid "Lines" [e7] #gridLines rows=1000
    - columns: Id | Product | Amount
    - row "1 | Widget | 10.00" [e8]
    - ... 980 more items
  - button "Save" [e9] #btnSave disabled
```

Attributes: `disabled`, `focused`, `modal`, `minimized`, `checked`/`unchecked`/`mixed`, `selected`, `expanded`/`collapsed`, `value="…"` (passwords show `value=***`), `value=N (min-max)` for sliders, `readonly`, `rows=N`, `scrollable`.

**Ref.** `e12` identifies one element and **stays valid across Snapshots** while that element exists. When the element is gone, using the Ref fails with `STALE_REF`.

**Selector.** Targets an element without a Snapshot. Names are case-insensitive.

| Selector | Matches |
|---|---|
| `#btnSave` | AutomationId `btnSave` |
| `button "Save"` | a button named exactly "Save" |
| `textbox ~"mail"` | a text box whose name contains "mail" |
| `button#btnSave` | both conditions |
| `item:nth(2)` | the second match (1-based) |
| `window "Confirm" >> button "Yes"` | a "Yes" button inside the "Confirm" window |

Roles: `button`, `checkbox`, `combobox`, `textbox` (alias `edit`), `link`, `image`, `item`, `list`, `menu`, `menubar`, `menuitem`, `progressbar`, `radio`, `slider`, `spinner`, `statusbar`, `tabs`, `tab`, `text` (alias `label`), `toolbar`, `tree`, `treeitem`, `grid`, `row`, `table`, `columnheader`, `window` (alias `dialog`), `pane`, `group`, `document`, `custom`, `element` (any).

A Selector that matches several elements fails with `AMBIGUOUS_SELECTOR` and lists them with their Refs.

**Actions.** `click`, `type`, `set_state`, `press_key`, `scroll` and `window` all follow the same steps:

1. Wait (up to `--action-timeout`, 5 s by default) for the target to exist and be **Actionable**, meaning enabled and on screen. Offscreen elements are scrolled into view.
2. Perform the action in the **Background** through UI Automation patterns, without taking focus, mouse or keyboard from the user.
3. Wait until the UI is **Settled**, meaning it has stopped changing (up to `--settle-timeout`, 3 s by default).
4. Return a **Change Report**:

```
clicked button "Submit" [e17] #btnSubmit
+ window "Confirm" [e31] modal
  + text "Are you sure?"
  + button "Yes" [e32] #btnYes
~ text "Saving..." #lblStatus
- button "Cancel" [e20]
focus: button "Yes" [e32] #btnYes
```

`+` added, `-` removed, `~` changed, `focus:` focus moved. Read the report instead of taking a new Snapshot after every action.

**Foreground Action.** Some input cannot be delivered in the background: key presses, right and double clicks, and clicks on elements that have no click pattern. Those tools return `NEEDS_FOREGROUND`. Pass `foreground: true` to briefly bring the window forward and use the real mouse or keyboard. Netwright then returns focus and the cursor to where the user left them. The exception is when the action opened a menu or dialog, which would close if it lost focus.

---

## desktop_app

Launch, attach to, close, or inspect the Target App. A session has one Target App; windows of its child processes belong to it.

| Parameter | Description |
|---|---|
| `action` | `launch`, `attach`, `close`, `status` |
| `path` | launch: executable to start |
| `project` | launch: `.csproj`/`.vbproj`/`.fsproj` to build (`dotnet msbuild`, Debug) and start |
| `args` | launch: command-line arguments |
| `framework` | launch: target framework of a multi-targeted project |
| `process` | attach: process id, process name, or part of a window title |
| `force` | close: kill instead of asking the app to close |

```
> desktop_app action=launch project=src/MyApp/MyApp.csproj
Launched MyApp.exe (pid 18244, WPF, launched by Netwright)
- window "My App" [e1]
```

A non-forced close that does not finish within 5 s (for example because of a "save changes?" prompt) fails with `TIMEOUT` and leaves the app running. Apps launched by Netwright are killed when the server stops; attached apps are left running.

## desktop_snapshot

| Parameter | Description |
|---|---|
| `root` | Ref or Selector: show only this subtree |
| `full` | Show every element, including layout panes, scrollbars and composite parts |
| `include_offscreen` | Include elements scrolled out of view |

The default view keeps interactive elements, text and named containers. It collapses unnamed layout panes, summarizes grid rows, shows at most 20 items per list and 300 lines, and leaves out offscreen elements.

## desktop_find

`selector`: lists up to 30 matches with Refs. Use it to disambiguate or to count.

## desktop_inspect

`target`: returns the element line, a **stable Selector** (AutomationId first), the path from the window, class and framework, bounds, supported patterns, slider range, and value.

## desktop_click

| Parameter | Description |
|---|---|
| `target` | Ref or Selector |
| `button` | `left` (default), `right`, `middle` |
| `double_click` | double click |
| `foreground` | use the real mouse |

In the background a left click uses the first supported option: Invoke, Toggle, SelectionItem, ExpandCollapse, then the legacy default action.

## desktop_type

| Parameter | Description |
|---|---|
| `target` | Ref or Selector |
| `text` | text to set |
| `clear` | replace the existing text (default `true`) |
| `submit` | press Enter afterwards (needs `foreground`) |
| `foreground` | type real keystrokes (for apps that validate on key events) |

In the background the text is set through the Value pattern.

## desktop_set_state

Set exactly one of:

| Parameter | Effect |
|---|---|
| `checked` | check boxes, toggle buttons, radio buttons (`true` selects) |
| `selected` | list, tab and tree items |
| `expanded` | tree items, expanders, combo boxes |
| `value` | sliders (number) or inputs (text) |
| `item` | select a child by name in a list, combo box, tab control or tree. Combo boxes are opened and closed as needed; if no item matches, the error lists the available items |

## desktop_press_key

| Parameter | Description |
|---|---|
| `keys` | `Enter`, `Ctrl+S`, `Alt+F4`, `Tab Tab Enter`: key names are Enter, Tab, Escape, Space, Backspace, Delete, Insert, Home, End, PageUp, PageDown, Up/Down/Left/Right, F1–F24, A–Z, 0–9, combined with Ctrl/Alt/Shift/Win |
| `target` | focus this element first |
| `foreground` | must be `true` |

## desktop_scroll

| Parameter | Description |
|---|---|
| `target` | a scrollable container, or any element inside one |
| `direction` | `up`, `down`, `left`, `right` |
| `amount` | `small` or `page` (default) |
| `to` | `top`, `bottom`, `start`, `end` |
| `into_view` | scroll the target itself into view |

## desktop_window

| Parameter | Description |
|---|---|
| `action` | `list`, `activate` (needs `foreground`), `minimize`, `maximize`, `restore`, `close` |
| `target` | a window or an element inside it (default: the main window) |

## desktop_screenshot

| Parameter | Description |
|---|---|
| `target` | window or element (default: the main window) |
| `max_size` | longest side in pixels (default 1280) |

Returns PNG image content. Windows are rendered with `PrintWindow`, so the capture is correct even when other windows cover the app. Minimized windows cannot be captured; restore them first.

## desktop_wait

| Parameter | Description |
|---|---|
| `target` | Ref or Selector |
| `state` | `visible` (default), `hidden`, `enabled`, `disabled`, `exists`, `gone` |
| `text` | instead of a target: text (name or value) that must appear anywhere, or disappear with `state=gone` |
| `timeout_ms` | default 10000 |

Returns how long it waited and a Change Report of everything that changed meanwhile.

## desktop_expect

| Parameter | Description |
|---|---|
| `target` | Ref or Selector |
| `assertion` | `exists`, `gone`, `visible`, `hidden`, `enabled`, `disabled`, `checked`, `unchecked`, `selected`, `not_selected`, `expanded`, `collapsed`, `focused`, `text`, `text_contains`, `value`, `count` |
| `expected` | for `text`, `text_contains`, `value`, and `count` (Selector matches) |
| `timeout_ms` | retry for up to this long (default 2000) |

```
> desktop_expect target=#lblResult assertion=text_contains expected=Saved
pass: text "Saved order 42" #lblResult containing "Saved"
```

A failed expectation is an `EXPECTATION_FAILED` error that states what was actually found.

## desktop_logs

| Parameter | Description |
|---|---|
| `stream` | `stdout`, `stderr` or `debug` (default: all) |
| `all` | include lines already returned |

Returns App Output lines not returned before:
- standard output and error when Netwright launched the app;
- `Trace`/`Debug`/`OutputDebugString` output for launched and attached apps, delivered only while no debugger is attached to the app.

**Crash Reports** do not need this tool. When the Target App dies from an unhandled exception, the next tool response starts with the exception and stack trace, read from the Windows event log or stderr:

```
! Target App exited unexpectedly (exit code 0xE0434352, unhandled .NET exception)
  System.InvalidOperationException: Order has no lines
     at MyApp.OrderViewModel.Save() in C:\src\MyApp\OrderViewModel.cs:line 42
```

## desktop_export_test

| Parameter | Description |
|---|---|
| `name` | Test name; becomes the class (`CheckoutSucceedsTests`) and method (`Checkout_succeeds`) names |
| `framework` | `xunit` (default), `nunit`, `mstest` |
| `namespace` | Namespace of the generated file (default `UiTests`) |

Returns a C# test that replays, through [Netwright.Testing](../src/Netwright.Testing/README.md), everything the Agent did successfully since the app was launched or attached:
- actions;
- waits;
- Expectations, which become assertions.

Every element is targeted by the stable Selector computed when the step ran. Refs never appear, and AutomationIds are preferred.

```csharp
[Fact]
public async Task Checkout_succeeds()
{
    await using var app = await DesktopApp.LaunchProjectAsync(@"src\Orders\Orders.csproj", null);

    await app.TypeAsync("#txtCustomer", "Rossi");
    await app.SelectAsync("#cmbCountry", "Italy");
    await app.ClickAsync("#btnSave");
    await app.Expect("#lblStatus").ToContainTextAsync("saved");
}
```

## Error codes

| Code | Meaning |
|---|---|
| `NO_APP` | No Target App; launch or attach first |
| `APP_EXITED` | The Target App has exited |
| `APP_NOT_RESPONDING` | UI Automation calls time out (app busy or hung) |
| `NOT_ALLOWED` | The app is not in the server's `--allow` list |
| `LAUNCH_FAILED` / `BUILD_FAILED` | The process could not start / the project did not build (errors included) |
| `ELEMENT_NOT_FOUND` | No element matches the Selector (after waiting) |
| `AMBIGUOUS_SELECTOR` | Several elements match; the candidates are listed |
| `INVALID_SELECTOR` | Selector syntax error |
| `STALE_REF` | The Ref's element no longer exists |
| `NOT_ACTIONABLE` | The element stayed disabled or offscreen |
| `NEEDS_FOREGROUND` | Retry with `foreground: true` |
| `NOT_SUPPORTED` | The element does not support the operation (see `desktop_inspect`) |
| `EXPECTATION_FAILED` | `desktop_expect` did not pass |
| `TIMEOUT` | A wait or close did not complete in time |
| `INVALID_ARGUMENT` | Missing or invalid parameter |
