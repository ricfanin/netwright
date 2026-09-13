# Captures exclude offscreen elements, with a fallback on misses

Every Snapshot, action and settle check captures the Target App's UI through UI Automation, so capture cost dominates latency. Non-virtualized controls make it explode: a WinForms `DataGridView` with 1000 rows exposes about 4000 elements, and a full capture took 8 s. Asking the provider to filter out `IsOffscreen` elements brings that capture to 1.7 s, because only the visible rows are marshalled. For ordinary forms the cost is unchanged or lower.

Captures therefore exclude offscreen elements by default. Some operations need hidden elements:

- A Selector or Ref may point at something scrolled out of view.
- The `count` Expectation counts every match.
- `desktop_snapshot include_offscreen=true` asks for them.

In those cases Netwright captures again with offscreen elements included, which keeps behaviour correct while leaving the common path fast.

## Consequences

- Default Snapshots no longer say how many offscreen items a container holds. Grids still show `rows=N`, and scrollable containers are marked `scrollable`.
- A target that does not exist costs two captures before it is reported as not found.
