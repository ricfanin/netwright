# Packs Netwright and installs it as a global dotnet tool from the local package.
# Usage: .\scripts\install-local.ps1

$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

Write-Host 'Installing Netwright as a global tool...' -ForegroundColor Cyan
dotnet tool uninstall --global Netwright 2>$null | Out-Null

& "$PSScriptRoot\pack.ps1"
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Pack failed, cannot install.' -ForegroundColor Red
    exit 1
}

dotnet tool install --global --add-source ./artifacts/nupkg Netwright --prerelease
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Installation failed.' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host "Installed. Add this to your MCP client configuration:" -ForegroundColor Green
Write-Host @'
{
  "mcpServers": {
    "netwright": { "command": "netwright" }
  }
}
'@
