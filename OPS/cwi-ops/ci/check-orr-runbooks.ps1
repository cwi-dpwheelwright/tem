param(
  [string]$RunbookRoot = "ops/cwi-ops/runbooks/orr"
)

$ErrorActionPreference = "Stop"

$required = @(
  "orchestrator.md",
  "router.md",
  "pack-service.md",
  "gateway.md"
)

foreach ($name in $required) {
  $path = Join-Path $RunbookRoot $name
  if (-not (Test-Path $path)) {
    Write-Host "Missing ORR runbook: $path"
    exit 1
  }
}

Write-Host "OK"
