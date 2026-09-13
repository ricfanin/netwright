# Actions wait for Actionable before and Settled after

Version 1 followed "No Automatic Retries": an action on a disabled or still-loading element failed immediately, and the Agent had to decide whether to try again. That cost a round trip and tokens on almost every asynchronous screen.

It also made Change Reports unreliable, because they were computed before the UI had finished reacting.

Starting with 2.0, every action does two bounded, configurable waits:

1. Before acting, it waits for the element to become Actionable.
2. After acting, it waits for the Target App to be Settled before building the Change Report.

The action itself still runs exactly once. We still don't retry an action after it has been performed, because repeating a side effect is unsafe.
