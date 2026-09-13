# One engine shared by the MCP server and the testing library

A Test Export has to behave exactly as the session the Agent verified. That covers Selector resolution, waiting for Actionable and Settled, and Background behaviour. Generated code that calls FlaUI directly would drift from that behaviour.

We therefore split the solution into three parts:

- **Netwright.Engine** holds all automation logic: UI Automation access, Snapshot, Refs, Selectors, waits, Change Reports, and Expectations.
- **Netwright**, the MCP server, is a thin tool layer over the Engine.
- **Netwright.Testing** is the public library that exported tests call. It is also built on the Engine.

We rejected emitting raw FlaUI code even though it would avoid publishing a library of our own: it would silently change timing and element resolution in the exported tests.
