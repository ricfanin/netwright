# Builds the solution and runs the unit tests.
# Usage: .\scripts\build.ps1 [-Release] [-Integration]
#   -Integration  also runs the UI integration tests (launches the Fixture Apps on this desktop)

param(
    [switch]$Release,
    [switch]$Integration
)

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)
$config = if ($Release) { 'Release' } else { 'Debug' }

Write-Host "Building Netwright ($config)..." -ForegroundColor Cyan
dotnet build Netwright.slnx -c $config
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host 'Running unit tests...' -ForegroundColor Cyan
dotnet test tests/Netwright.Engine.Tests -c $config --no-build
if ($LASTEXITCODE -ne 0) { exit 1 }
dotnet test tests/Netwright.Testing.Tests -c $config --no-build
if ($LASTEXITCODE -ne 0) { exit 1 }

if ($Integration) {
    Write-Host 'Building the Fixture Apps...' -ForegroundColor Cyan
    foreach ($fixture in 'Netwright.Fixtures.Wpf', 'Netwright.Fixtures.WinForms', 'Netwright.Fixtures.WinUI') {
        dotnet build "tests/fixtures/$fixture" -c Release
        if ($LASTEXITCODE -ne 0) { exit 1 }
    }

    Write-Host 'Publishing the server for end-to-end tests...' -ForegroundColor Cyan
    dotnet publish src/Netwright -c Release -r win-x64 -o artifacts/netwright
    if ($LASTEXITCODE -ne 0) { exit 1 }

    Write-Host 'Running integration tests (the Fixture Apps will open on this desktop)...' -ForegroundColor Cyan
    dotnet test tests/Netwright.IntegrationTests -c $config --no-build --filter 'Category!=Perf'
    if ($LASTEXITCODE -ne 0) { exit 1 }
}

Write-Host 'Done.' -ForegroundColor Green
