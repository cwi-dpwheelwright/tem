param(
  [Parameter(Mandatory = $false)]
  [string]$ComposeFile = "OPS/cwi-ops/compose/test/docker-compose.test.yml",
  [Parameter(Mandatory = $true)]
  [string]$Service,
  [Parameter(Mandatory = $false)]
  [int]$DurationSeconds = 60
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ComposeFile)) {
  throw "Compose file not found: $ComposeFile"
}

Write-Host "Stopping service $Service for $DurationSeconds seconds..."
cmd /c "docker compose -f `"$ComposeFile`" stop $Service 2>&1" | Out-Host
if ($LASTEXITCODE -ne 0) {
  throw "docker compose stop failed with exit code $LASTEXITCODE"
}
Start-Sleep -Seconds $DurationSeconds
Write-Host "Starting service $Service..."
cmd /c "docker compose -f `"$ComposeFile`" start $Service 2>&1" | Out-Host
if ($LASTEXITCODE -ne 0) {
  throw "docker compose start failed with exit code $LASTEXITCODE"
}
