# Creates the Netwright NuGet packages (the MCP server tool, plus the Engine library) in artifacts/nupkg.
# Usage: .\scripts\pack.ps1

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

Write-Host 'Creating NuGet packages...' -ForegroundColor Cyan
if (Test-Path ./artifacts/nupkg) {
    Remove-Item -Recurse -Force ./artifacts/nupkg
}

dotnet pack src/Netwright -c Release -o ./artifacts/nupkg
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Pack failed.' -ForegroundColor Red
    exit 1
}

foreach ($project in 'src/Netwright.Engine', 'src/Netwright.Testing') {
    dotnet pack $project -c Release -o ./artifacts/nupkg
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Pack failed.' -ForegroundColor Red
        exit 1
    }
}

Write-Host 'Packages:' -ForegroundColor Green
Get-ChildItem ./artifacts/nupkg/*.nupkg | ForEach-Object { Write-Host "  $($_.Name)" }
Write-Host ''
Write-Host 'Try it without installing:  dnx Netwright --add-source ./artifacts/nupkg --yes' -ForegroundColor Gray
