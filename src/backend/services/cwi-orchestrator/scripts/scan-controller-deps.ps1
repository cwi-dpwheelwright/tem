param(
  [string]$Root = "src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api"
)

$ErrorActionPreference = "Stop"

$matches = Get-ChildItem -Recurse -Path $Root -Filter *.cs |
  Select-String -Pattern "DbContext|DataAccess" -CaseSensitive

if ($matches) {
  Write-Host "Found forbidden references in API layer:"
  $matches | ForEach-Object { Write-Host $_.Path ":" $_.LineNumber ":" $_.Line }
  exit 1
}

Write-Host "OK"
