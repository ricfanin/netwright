# Contributing to Netwright

Thanks for helping! Netwright is small enough that a good bug report with a repro often matters as much as code.

## Setup

- Windows 10/11
- .NET 10 SDK (`global.json` pins the major version) and the .NET 8 Desktop Runtime (the Fixture Apps target net8.0)
- Visual Studio 2022 17.14+, Rider, or VS Code with C# Dev Kit

```powershell
git clone https://github.com/ricfanin/netwright.git
cd netwright
.\scripts\build.ps1               # build + unit tests
.\scripts\build.ps1 -Integration  # also runs the UI integration tests (Fixture Apps open on your desktop)
```

## Where things live

| Path | What |
|---|---|
| `CONTEXT.md` | The domain glossary. Use its terms in code, docs and issues |
| `docs/adr/` | Why the architecture is the way it is. Read before proposing a change to an area they cover |
| `src/Netwright.Engine` | Automation engine (no MCP dependency) |
| `src/Netwright` | MCP server: thin tool layer over the Engine |
| `src/Netwright.Testing` | Library used by exported tests |
| `tests/Netwright.Engine.Tests` | Fast unit tests on in-memory trees |
| `tests/Netwright.IntegrationTests` | Engine and MCP server against the Fixture Apps |
| `tests/fixtures` | Fixture Apps implementing [FIXTURE-CONTRACT.md](tests/fixtures/FIXTURE-CONTRACT.md) |
| `benchmarks` | Token and latency benchmark (`scripts/benchmark.ps1`) |

## Guidelines

- **Test-first where possible.** Rendering, selectors, diffs and parsers are pure; cover them with unit tests. Behaviour that depends on a UI framework needs an integration test against the Fixture Apps. If the scenario is missing, extend the contract and every Fixture App.
- **Measure performance claims.** Use `CapturePerformanceProbe` (`--filter Category=Perf`) and the benchmark, and include before/after numbers in the PR.
- **Watch token cost.** Tool descriptions and responses are paid for on every turn. Run the benchmark when you change tool definitions or output formats.
- **Errors are for agents.** Throw `NetwrightException` with a stable code and a hint that says what to do next.
- **Stay in the background.** New actions must work through UI Automation patterns without focus, or refuse with `NEEDS_FOREGROUND` and support `foreground: true`.
- Warnings are errors (`TreatWarningsAsErrors`); follow the existing code style.

## Pull requests

1. Open an issue first for anything bigger than a bug fix.
2. Branch from `main`, keep commits focused, and use conventional messages (`feat:`, `fix:`, `docs:`, `perf:`, `test:`).
3. `.\scripts\build.ps1 -Integration` must pass locally. CI runs build and unit tests; integration tests on hosted runners are best effort.
4. Update `docs/TOOLS_REFERENCE.md` for tool changes, and add an ADR for decisions that are hard to reverse.
