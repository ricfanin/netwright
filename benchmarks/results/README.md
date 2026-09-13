# Netwright Benchmark

- Machine: `AMD64 Family 23 Model 113 Stepping 0, AuthenticAMD, 16 logical cores` — Microsoft Windows 10.0.26200
- Token encoding: `o200k_base` (proxy for Claude's tokenizer; the same encoding is used for every server)
- Iterations per scenario: 5; latency is the sum of tool-call round trips, excluding Agent think time and fixed polling sleeps

## Tool definitions (sent to the model on every turn)

| Server | Fixture | Tools | Tokens |
|---|---|---:|---:|
| FlaUI-MCP | winforms | 12 | 1,418 |
| FlaUI-MCP | wpf | 12 | 1,418 |
| WPF-MCP 1.0 | winforms | 24 | 2,023 |
| WPF-MCP 1.0 | wpf | 24 | 2,023 |
| Netwright 2.0 | winforms | 15 | 1,387 |
| Netwright 2.0 | wpf | 15 | 1,387 |
| Windows-MCP | winforms | 20 | 3,984 |
| Windows-MCP | wpf | 20 | 3,984 |

## `snapshot-form`

| Server | Fixture | Success | Calls | Tokens (median) | Latency p50 | Latency p95 |
|---|---|---:|---:|---:|---:|---:|
| FlaUI-MCP | winforms | 5/5 | 1 | 622 | 96 ms | 112 ms |
| FlaUI-MCP | wpf | 5/5 | 1 | 785 | 130 ms | 157 ms |
| WPF-MCP 1.0 | winforms | 5/5 | 1 | 816 | 133 ms | 234 ms |
| WPF-MCP 1.0 | wpf | 5/5 | 1 | 1,023 | 169 ms | 276 ms |
| Netwright 2.0 | winforms | 5/5 | 1 | 396 | 62 ms | 66 ms |
| Netwright 2.0 | wpf | 5/5 | 1 | 385 | 80 ms | 83 ms |
| Windows-MCP | winforms | 5/5 | 1 | 1,897 | 313 ms | 331 ms |
| Windows-MCP | wpf | 5/5 | 1 | 1,956 | 332 ms | 660 ms |

## `snapshot-grid`

| Server | Fixture | Success | Calls | Tokens (median) | Latency p50 | Latency p95 |
|---|---|---:|---:|---:|---:|---:|
| FlaUI-MCP | winforms | 5/5 | 1 | 107,136 | 12,982 ms | 13,216 ms |
| FlaUI-MCP | wpf | 5/5 | 1 | 6,234 | 776 ms | 784 ms |
| WPF-MCP 1.0 | winforms | 5/5 | 1 | 3,167 | 9,206 ms | 10,228 ms |
| WPF-MCP 1.0 | wpf | 5/5 | 1 | 3,988 | 882 ms | 1,110 ms |
| Netwright 2.0 | winforms | 5/5 | 1 | 643 | 1,543 ms | 1,563 ms |
| Netwright 2.0 | wpf | 5/5 | 1 | 627 | 378 ms | 388 ms |
| Windows-MCP | winforms | 5/5 | 1 | 1,897 | 311 ms | 314 ms |
| Windows-MCP | wpf | 5/5 | 1 | 1,897 | 310 ms | 312 ms |

## `form-submit`

| Server | Fixture | Success | Calls | Tokens (median) | Latency p50 | Latency p95 |
|---|---|---:|---:|---:|---:|---:|
| FlaUI-MCP | winforms | 0/5 | 10 | 2,615 | 620 ms | 630 ms |
| FlaUI-MCP | wpf | 5/5 | 10 | 3,469 | 721 ms | 749 ms |
| WPF-MCP 1.0 | winforms | 0/5 | 9 | 1,240 | 363 ms | 424 ms |
| WPF-MCP 1.0 | wpf | 5/5 | 10 | 2,463 | 534 ms | 689 ms |
| Netwright 2.0 | winforms | 5/5 | 6 | 586 | 1,868 ms | 1,897 ms |
| Netwright 2.0 | wpf | 5/5 | 6 | 549 | 1,832 ms | 1,871 ms |
| Windows-MCP | winforms | not measured¹ | | | | |
| Windows-MCP | wpf | not measured¹ | | | | |

## `async-load`

| Server | Fixture | Success | Calls | Tokens (median) | Latency p50 | Latency p95 |
|---|---|---:|---:|---:|---:|---:|
| FlaUI-MCP | winforms | 5/5 | 5 | 1,771 | 278 ms | 288 ms |
| FlaUI-MCP | wpf | 5/5 | 5 | 2,484 | 329 ms | 344 ms |
| WPF-MCP 1.0 | winforms | 5/5 | 5 | 2,358 | 248 ms | 314 ms |
| WPF-MCP 1.0 | wpf | 5/5 | 5 | 3,337 | 398 ms | 467 ms |
| Netwright 2.0 | winforms | 5/5 | 3 | 437 | 1,778 ms | 1,795 ms |
| Netwright 2.0 | wpf | 5/5 | 3 | 405 | 1,817 ms | 1,844 ms |
| Windows-MCP | winforms | not measured¹ | | | | |
| Windows-MCP | wpf | not measured¹ | | | | |

## `screenshot`

| Server | Fixture | Success | Calls | Tokens (median) | Latency p50 | Latency p95 |
|---|---|---:|---:|---:|---:|---:|
| FlaUI-MCP | winforms | 5/5 | 1 | 1,000 | 28 ms | 40 ms |
| FlaUI-MCP | wpf | 5/5 | 1 | 1,000 | 25 ms | 40 ms |
| WPF-MCP 1.0 | winforms | 5/5 | 1 | 18,825 | 33 ms | 65 ms |
| WPF-MCP 1.0 | wpf | 5/5 | 1 | 25,073 | 32 ms | 135 ms |
| Netwright 2.0 | winforms | 5/5 | 1 | 1,024 | 99 ms | 112 ms |
| Netwright 2.0 | wpf | 5/5 | 1 | 1,020 | 98 ms | 109 ms |
| Windows-MCP | winforms | 5/5 | 1 | 1,944 | 341 ms | 376 ms |
| Windows-MCP | wpf | 5/5 | 1 | 1,944 | 389 ms | 1,491 ms |

¹ The server's tools act at screen coordinates with the real mouse and keyboard, so scripted multi-step scenarios were not run against it.

Windows-MCP snapshots describe the whole desktop (interactive elements of visible windows with coordinates) rather than the app's tree, so their token counts are not directly comparable: the grid snapshot, for example, does not list the grid's rows.
