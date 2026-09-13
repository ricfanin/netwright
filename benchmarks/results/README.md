# Netwright Benchmark

- Machine: `AMD64 Family 23 Model 113 Stepping 0, AuthenticAMD, 16 logical cores` — Microsoft Windows 10.0.26200
- Token encoding: `o200k_base` (proxy for Claude's tokenizer; the same encoding is used for every server)
- Iterations per scenario: 5; latency is the sum of tool-call round trips, excluding Agent think time and fixed polling sleeps

## Tool definitions (sent to the model on every turn)

| Server | Fixture | Tools | Tokens |
|---|---|---:|---:|
| WPF-MCP 1.0 | wpf | 24 | 2,023 |
| WPF-MCP 1.0 | winforms | 24 | 2,023 |

## `snapshot-form`

| Server | Fixture | Success | Calls | Tokens (median) | Latency p50 | Latency p95 |
|---|---|---:|---:|---:|---:|---:|
| WPF-MCP 1.0 | wpf | 5/5 | 1 | 1,023 | 169 ms | 276 ms |
| WPF-MCP 1.0 | winforms | 5/5 | 1 | 816 | 133 ms | 234 ms |

## `snapshot-grid`

| Server | Fixture | Success | Calls | Tokens (median) | Latency p50 | Latency p95 |
|---|---|---:|---:|---:|---:|---:|
| WPF-MCP 1.0 | wpf | 5/5 | 1 | 3,988 | 882 ms | 1,110 ms |
| WPF-MCP 1.0 | winforms | 5/5 | 1 | 3,167 | 9,206 ms | 10,228 ms |

## `form-submit`

| Server | Fixture | Success | Calls | Tokens (median) | Latency p50 | Latency p95 |
|---|---|---:|---:|---:|---:|---:|
| WPF-MCP 1.0 | wpf | 5/5 | 10 | 2,463 | 534 ms | 689 ms |
| WPF-MCP 1.0 | winforms | 0/5 | 9 | 1,240 | 363 ms | 424 ms |

## `async-load`

| Server | Fixture | Success | Calls | Tokens (median) | Latency p50 | Latency p95 |
|---|---|---:|---:|---:|---:|---:|
| WPF-MCP 1.0 | wpf | 5/5 | 5 | 3,337 | 398 ms | 467 ms |
| WPF-MCP 1.0 | winforms | 5/5 | 5 | 2,358 | 248 ms | 314 ms |

## `screenshot`

| Server | Fixture | Success | Calls | Tokens (median) | Latency p50 | Latency p95 |
|---|---|---:|---:|---:|---:|---:|
| WPF-MCP 1.0 | wpf | 5/5 | 1 | 25,073 | 32 ms | 135 ms |
| WPF-MCP 1.0 | winforms | 5/5 | 1 | 18,825 | 33 ms | 65 ms |

