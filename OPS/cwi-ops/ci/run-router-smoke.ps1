# Add router smoke check to CI helper

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$smoke = Join-Path $root "..\scripts\smoke-router.ps1"

if (-not (Test-Path $smoke)) {
  Write-Host "Missing smoke script at $smoke"
  exit 1
}

& $smoke
