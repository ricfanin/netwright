---
status: accepted (amends ADR 0001)
---

# Run Background actions in-process through a Companion loaded by a startup hook

ADR 0001 kept the server out-of-process on UI Automation and deferred an in-process component. Two measurements, taken on 2026-10-08 against the WPF and WinForms Fixture Apps, showed that the Background promise did not hold without one:

- **Cross-process pattern calls hand the app the foreground.** A Fixture App was launched without activation while another window was in the foreground. One call to the Value, Toggle, Invoke, SelectionItem or ExpandCollapse pattern then made it the foreground window, in both WPF and WinForms. RangeValue did not. The same provider calls made from inside the app never did (8 of 8 cases). The cross-process path is what hands over the foreground, not the controls' own code.
- **Text set without the keyboard never commits focus-bound bindings.** WPF `TextBox.Text` defaults to `UpdateSourceTrigger.LostFocus`, and WinForms bindings default to `DataSourceUpdateMode.OnValidation`. With either, the view model keeps the old value. Moving focus from outside the app with UIA `SetFocus` also hands the app the foreground.

Apps launched by Netwright, or by Netwright.Testing, now load the **Companion**, a small `Netwright.Companion` assembly, through `DOTNET_STARTUP_HOOKS`. This is a supported .NET mechanism, not code injection. The Companion serves a named pipe that only the current user can open. When the Engine asks, it runs the pattern in-process, on WPF peers (found by RuntimeId, which WPF builds as `[7, pid, peer hash]`) or on WinForms controls (found by window handle). After setting text, it commits pending LostFocus and OnValidation bindings.

UI Automation stays the source of truth for observing the UI: Snapshots, Refs, Selectors, the Change Report. The Companion only performs actions. The Engine uses UI Automation directly when there is no Companion, when the Companion does not know the element, or for an attached app. In that case a guard gives the foreground back to the User's window if the Target App took it, and the outcome says so.

## Consequences

- The Background promise holds for apps launched by Netwright on .NET 8 or later, in WPF and WinForms.
- It holds only partially in these cases:
  - attached apps;
  - .NET Framework apps;
  - .NET before 8;
  - WinUI, which the Companion does not handle yet.

  There the Target App may take the foreground for a moment before Netwright gives it back, and the Agent is told once per session that bindings may not be committed.
- The Companion handles only a fixed list of requests: commit bindings, invoke, toggle, select, deselect, expand, collapse, set value, set range, and select an item. It never runs arbitrary code, and it swallows every failure so it cannot take the Target App down.
- The startup hook is inherited by the app's child processes, which load their own Companion. Startup hooks the app already sets are kept.
- Key presses, double clicks and right clicks still need a Foreground Action. The Companion is the channel through which they can later move to the Background.
- The Companion targets `net8.0-windows` and references WPF and WinForms, but it loads a UI stack only if the app has already loaded it.
