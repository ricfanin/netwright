---
status: superseded by ADR 0006
---

# Keep FlaUI; no Native AOT

Native AOT would give faster startup and a smaller package. However, FlaUI.UIA3 relies on built-in COM interop, which NativeAOT does not support (FlaUI issue #672 has been open with no response since 2025).

Getting AOT would mean replacing FlaUI with our own `[GeneratedComInterface]` UI Automation bindings. We would then have to reimplement input, capture, and the control patterns ourselves.

We keep FlaUI and ship self-contained ReadyToRun builds. On hot paths (Snapshot, find, wait) we call the native UI Automation cache APIs directly, because the measured cost is cross-process COM calls, not FlaUI itself.

We will revisit this only if the Benchmark shows startup time or FlaUI overhead actually matters.
