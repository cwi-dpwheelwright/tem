param(
  [string]$ComposeFile = "OPS/cwi-ops/compose/test/docker-compose.test.yml",
  [string]$EnvFile = "OPS/cwi-ops/compose/test/.env",
  [string]$Profile = "integration",
  [int]$StartupWaitSeconds = 60
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ComposeFile)) {
  Write-Host "Compose file not found: $ComposeFile"
  exit 1
}

if (-not (Test-Path $EnvFile)) {
  Write-Host "Env file not found: $EnvFile"
  exit 1
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\\..\\.."))

try {
  docker compose -f $ComposeFile --env-file $EnvFile --profile $Profile up -d

  Write-Host "Waiting for router health..."
  $deadline = (Get-Date).AddSeconds($StartupWaitSeconds)
  $ready = $false
  while ((Get-Date) -lt $deadline) {
    try {
      $health = Invoke-RestMethod -TimeoutSec 2 -Uri "http://localhost:18081/api/health"
      if ($health.ok -eq $true) {
        $ready = $true
        break
      }
    } catch {
    }
    Start-Sleep -Seconds 2
  }

  if (-not $ready) {
    Write-Host "Router health check did not succeed within $StartupWaitSeconds seconds."
    exit 1
  }

  Write-Host "Running ETL ingest integration test..."
  docker run --rm --network cwi-test `
    -e CWI_ETL_INGEST_URL="http://cwi-router:8081/api/ingest/document" `
    -e CWI_VECTOR_SEARCH_URL=$env:CWI_VECTOR_SEARCH_URL `
    -e CWI_VECTOR_SEARCH_PAYLOAD_JSON=$env:CWI_VECTOR_SEARCH_PAYLOAD_JSON `
    -e CWI_ASK_URL=$env:CWI_ASK_URL `
    -e CWI_ASK_PAYLOAD_JSON=$env:CWI_ASK_PAYLOAD_JSON `
    -e CWI_TRACE_LOOKUP_URL_TEMPLATE="http://cwi-router:8081/api/trace/{trace_id}" `
    -e CWI_TENANT_ID="tenant-1" `
    -v "${repoRoot}:/repo" `
    -w /repo/tools/cwi-gold-tests `
    python:3.11-slim `
    bash -lc "python -m pip install -q pytest requests schemathesis && python -m pytest -m integration tests/test_etl_ingest.py"
}
finally {
  docker compose -f $ComposeFile --env-file $EnvFile --profile $Profile down
}
