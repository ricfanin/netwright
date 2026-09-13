# Netwright

Playwright-style automation for .NET desktop apps: an MCP server that lets an AI Agent see and operate the UI of .NET desktop applications, primarily so Developers can have the Agent verify and test their own apps. (Formerly WPF-MCP.)

## Language

### Actors

**Agent**:
The AI client (e.g. Claude Code) that calls the server's tools to observe and operate a Target App.
_Avoid_: client, LLM, bot

**User**:
The human sitting at the machine where the Target App runs, possibly working on something else while the Agent operates.
_Avoid_: operator, tester

**Developer**:
A User who owns the Target App's source code and can modify or instrument it; the primary audience.
_Avoid_: customer, integrator

### Automation

**Target App**:
The single .NET desktop application (on .NET or .NET Framework), together with the windows of its child processes, that the Agent is operating in a session.
_Avoid_: AUT, application under test, WPF app, attached app

**Unsupported App**:
A non-.NET desktop application (e.g. Delphi, Qt, Electron) that may work through the same mechanisms but receives no guarantees or fixes.
_Avoid_: third-party app, foreign app

**Support Tier**:
The level of commitment for a .NET UI technology: Tier 1 has Fixture App, CI, and Benchmark coverage; Tier 2 has Fixture App and integration tests; Tier 3 is best effort.
_Avoid_: compatibility level, support level

**Background**:
The default way of operating a Target App: without taking focus, mouse, or keyboard away from the User.
_Avoid_: headless, silent mode, invisible mode

**Foreground Action**:
A single tool call the Agent explicitly marks as allowed to take focus and input, after which focus is returned to where the User left it.
_Avoid_: foreground mode, fallback

**Snapshot**:
A compact textual representation of the Target App's UI, filtered by default to what the Agent needs to act and understand the screen.
_Avoid_: dump, accessibility tree, DOM

**Ref**:
A short identifier for one UI element that stays the same across Snapshots for as long as that element exists.
_Avoid_: handle, element id, selector

**Stale Ref**:
A Ref whose element no longer exists in the Target App.
_Avoid_: invalid ref, expired ref

**Selector**:
A short textual expression (by AutomationId, control type and name, or a chain of these) that identifies an element without a prior Snapshot.
_Avoid_: locator, query, XPath

**Actionable**:
The state of an element that is enabled, visible, and no longer moving, so an action on it can be performed.
_Avoid_: ready, interactable

**Settled**:
The state of the Target App's UI after an action once it has stopped changing for a short quiet period.
_Avoid_: idle, stable, loaded

**Change Report**:
The compact account, returned by an action, of what changed in the Target App once it Settled: elements added, removed, or modified, windows opened or closed, and focus moves.
_Avoid_: diff, delta, patch

**Expectation**:
A check the Agent asks for on an element's existence, text, state, or value, answered as pass or fail.
_Avoid_: assertion, verification, check

### Diagnostics

**App Output**:
The text the Target App emits while running: standard output and error when Netwright launched it, and debug/trace output in all cases.
_Avoid_: console messages, logs

**Crash Report**:
The exception type, message, and stack trace of a Target App that terminated unexpectedly, delivered to the Agent with its next tool call.
_Avoid_: crash log, error dump

### Test Export

**Test Export**:
A replayable automated test, in C#, generated from the actions and Expectations an Agent performed in a session, runnable without an Agent.
_Avoid_: recording, macro, script

### Quality

**Fixture App**:
A purpose-built Target App kept in this repository that exercises every supported control and scenario, used by integration tests and the Benchmark.
_Avoid_: sample app, demo app, test app

**Benchmark**:
A reproducible measurement of token cost, latency, and success of tool calls against the Fixture Apps, published for comparison across versions and competing tools.
_Avoid_: perf test, stress test
