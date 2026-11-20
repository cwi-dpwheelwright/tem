param(
  [string]$ComposeFile = "ops/cwi-ops/compose/test/docker-compose.test.yml"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ComposeFile)) {
  Write-Host "Missing compose test file: $ComposeFile"
  exit 1
}

$content = Get-Content $ComposeFile -Raw
foreach ($profile in @("smoke", "integration", "simulation")) {
  if ($content -notmatch $profile) {
    Write-Host "Missing profile '$profile' in $ComposeFile"
    exit 1
  }
}

Write-Host "OK"
