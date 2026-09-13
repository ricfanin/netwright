<#
.SYNOPSIS
    Builds the Fixture Apps and runs the Netwright Benchmark against one or more MCP servers.

.DESCRIPTION
    Results are written to benchmarks/results/<server>-<fixture>.json, and benchmarks/results/README.md
    is regenerated from every JSON file in that folder.

    Servers:
      v1           WPF-MCP 1.0, built from the last 1.x commit in a detached worktree under artifacts/
      v2           Netwright from this working copy
      flaui-mcp    github.com/shanselman/FlaUI-MCP, cloned and built under artifacts/competitors
      windows-mcp  github.com/CursorTouch/Windows-MCP via `uvx windows-mcp serve` (requires uv);
                   only read-only scenarios, because its actions move the real mouse

    The benchmark opens the Fixture Apps on this desktop. Avoid using the machine while it runs.

.EXAMPLE
    ./scripts/benchmark.ps1 -Servers v1,v2,flaui-mcp,windows-mcp -Fixtures wpf,winforms -Iterations 5
#>
param(
    [string[]] $Servers = @('v2'),
    [string[]] $Fixtures = @('wpf', 'winforms'),
    [int] $Iterations = 5,
    [string] $V1Commit = 'e96c57d'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$dotnet = if (Test-Path "$env:USERPROFILE\.dotnet\dotnet.exe") { "$env:USERPROFILE\.dotnet\dotnet.exe" } else { 'dotnet' }

$fixturePaths = @{
    wpf      = 'tests/fixtures/Netwright.Fixtures.Wpf/bin/Release/net8.0-windows/Netwright.Fixtures.Wpf.exe'
    winforms = 'tests/fixtures/Netwright.Fixtures.WinForms/bin/Release/net8.0-windows/Netwright.Fixtures.WinForms.exe'
}

Write-Host 'Building fixtures and benchmark harness...' -ForegroundColor Cyan
& $dotnet build tests/fixtures/Netwright.Fixtures.Wpf -c Release -v q | Out-Null
& $dotnet build tests/fixtures/Netwright.Fixtures.WinForms -c Release -v q | Out-Null
& $dotnet build benchmarks/Netwright.Benchmarks -c Release -v q | Out-Null

$definitions = @{}
if ($Servers -contains 'v1') {
    if (-not (Test-Path 'artifacts/v1/WpfMcp.Server.exe')) {
        Write-Host "Building WPF-MCP 1.0 from $V1Commit..." -ForegroundColor Cyan
        if (-not (Test-Path 'artifacts/v1-src')) { git worktree add --detach artifacts/v1-src $V1Commit | Out-Null }
        & $dotnet publish artifacts/v1-src/src/WpfMcp.Server -c Release -o artifacts/v1 -v q | Out-Null
    }
    $definitions['v1'] = @{ Label = 'WPF-MCP 1.0'; Adapter = 'v1'; Command = (Resolve-Path 'artifacts/v1/WpfMcp.Server.exe').Path; Args = @(); Protocol = $null }
}
if ($Servers -contains 'v2') {
    Write-Host 'Publishing Netwright...' -ForegroundColor Cyan
    & $dotnet publish src/Netwright -c Release -r win-x64 -o artifacts/netwright -v q | Out-Null
    $definitions['v2'] = @{ Label = 'Netwright 2.0'; Adapter = 'v2'; Command = (Resolve-Path 'artifacts/netwright/netwright.exe').Path; Args = @(); Protocol = $null }
}
if ($Servers -contains 'flaui-mcp') {
    if (-not (Test-Path 'artifacts/competitors/flaui-mcp-bin/FlaUI.Mcp.dll')) {
        Write-Host 'Building FlaUI-MCP...' -ForegroundColor Cyan
        New-Item -ItemType Directory -Force artifacts/competitors | Out-Null
        if (-not (Test-Path 'artifacts/competitors/FlaUI-MCP')) { git clone --depth 1 https://github.com/shanselman/FlaUI-MCP.git artifacts/competitors/FlaUI-MCP | Out-Null }
        & $dotnet publish artifacts/competitors/FlaUI-MCP/src/FlaUI.Mcp -c Release -o artifacts/competitors/flaui-mcp-bin -v q | Out-Null
    }
    $definitions['flaui-mcp'] = @{ Label = 'FlaUI-MCP'; Adapter = 'flaui-mcp'; Command = $dotnet; Args = @((Resolve-Path 'artifacts/competitors/flaui-mcp-bin/FlaUI.Mcp.dll').Path); Protocol = $null }
}
if ($Servers -contains 'windows-mcp') {
    $definitions['windows-mcp'] = @{ Label = 'Windows-MCP'; Adapter = 'windows-mcp'; Command = 'uvx'; Args = @('windows-mcp', 'serve'); Protocol = '2026-07-28' }
}

foreach ($server in $Servers) {
    $definition = $definitions[$server]
    if (-not $definition) { throw "Unknown server '$server'." }
    foreach ($fixture in $Fixtures) {
        $arguments = @('run', '--adapter', $definition.Adapter, '--command', $definition.Command, '--fixture', $fixturePaths[$fixture],
            '--fixture-name', $fixture, '--label', $definition.Label, '--iterations', $Iterations, '--out', "benchmarks/results/$server-$fixture.json")
        foreach ($a in $definition.Args) { $arguments += @('--arg', $a) }
        if ($definition.Protocol) { $arguments += @('--protocol', $definition.Protocol) }
        & $dotnet run --project benchmarks/Netwright.Benchmarks -c Release --no-build -- @arguments
    }
}

$results = Get-ChildItem benchmarks/results/*.json | Sort-Object Name | ForEach-Object { $_.FullName }
& $dotnet run --project benchmarks/Netwright.Benchmarks -c Release --no-build -- report --out benchmarks/results/README.md @results
