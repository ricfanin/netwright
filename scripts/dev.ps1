# Runs the MCP server from source with auto-rebuild on file changes.
# Usage: .\scripts\dev.ps1 [-- server options, e.g. --allow "MyApp*"]

Write-Host 'Starting Netwright in development mode (Ctrl+C to stop)...' -ForegroundColor Cyan
Set-Location (Split-Path -Parent $PSScriptRoot)
dotnet watch run --project src/Netwright -- @args
