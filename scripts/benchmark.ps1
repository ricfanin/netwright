<#
.SYNOPSIS
    Builds the Fixture Apps and runs the Netwright Benchmark against one or more MCP servers.

.DESCRIPTION
    Results are written to benchmarks/results/<server>-<fixture>.json, and a combined
    benchmarks/results/README.md is regenerated from every JSON file in that folder.

    The v1 baseline is built from the last 1.x commit in a detached worktree under artifacts/,
    so it never interferes with the working copy.

.EXAMPLE
    ./scripts/benchmark.ps1 -Servers v1,v2 -Fixtures wpf,winforms -Iterations 5
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

$serverCommands = @{}
if ($Servers -contains 'v1') {
    if (-not (Test-Path 'artifacts/v1/WpfMcp.Server.exe')) {
        Write-Host "Building v1 baseline from $V1Commit..." -ForegroundColor Cyan
        if (-not (Test-Path 'artifacts/v1-src')) {
            git worktree add --detach artifacts/v1-src $V1Commit | Out-Null
        }
        & $dotnet publish artifacts/v1-src/src/WpfMcp.Server -c Release -o artifacts/v1 -v q | Out-Null
    }
    $serverCommands['v1'] = @{ Label = 'WPF-MCP 1.0'; Adapter = 'v1'; Command = (Resolve-Path 'artifacts/v1/WpfMcp.Server.exe').Path; Args = @() }
}
if ($Servers -contains 'v2') {
    Write-Host 'Publishing Netwright...' -ForegroundColor Cyan
    & $dotnet publish src/Netwright -c Release -o artifacts/netwright -v q | Out-Null
    $serverCommands['v2'] = @{ Label = 'Netwright 2.0'; Adapter = 'v2'; Command = (Resolve-Path 'artifacts/netwright/netwright.exe').Path; Args = @() }
}

foreach ($server in $Servers) {
    $definition = $serverCommands[$server]
    if (-not $definition) { throw "Unknown server '$server'." }
    foreach ($fixture in $Fixtures) {
        $out = "benchmarks/results/$server-$fixture.json"
        $arguments = @('run', '--adapter', $definition.Adapter, '--command', $definition.Command, '--fixture', $fixturePaths[$fixture],
            '--fixture-name', $fixture, '--label', $definition.Label, '--iterations', $Iterations, '--out', $out)
        foreach ($a in $definition.Args) { $arguments += @('--arg', $a) }
        & $dotnet run --project benchmarks/Netwright.Benchmarks -c Release --no-build -- @arguments
    }
}

$results = Get-ChildItem benchmarks/results/*.json | Sort-Object Name | ForEach-Object { $_.FullName }
& $dotnet run --project benchmarks/Netwright.Benchmarks -c Release --no-build -- report --out benchmarks/results/README.md @results
