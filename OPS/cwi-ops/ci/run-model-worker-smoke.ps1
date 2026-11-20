param(
  [string]$ComposeFile = "OPS/cwi-ops/compose/gpu_worker/docker-compose.yml",
  [string]$ComposeOverride = "OPS/cwi-ops/compose/gpu_worker/docker-compose.smoke.yml",
  [string]$EnvFile = "OPS/cwi-ops/compose/gpu_worker/.env",
  [string]$HostBaseUrl = "http://localhost",
  [string]$Model = $env:CWI_MODEL_WORKER_MODEL,
  [int]$StartupWaitSeconds = 60
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ComposeFile)) {
  throw "Compose file not found: $ComposeFile"
}
if (-not (Test-Path $ComposeOverride)) {
  throw "Compose override not found: $ComposeOverride"
}
if (-not (Test-Path $EnvFile)) {
  throw "Env file not found: $EnvFile"
}
if (-not $Model) {
  $Model = $env:MODEL_NAME
}
if (-not $Model) {
  throw "Model is required. Set CWI_MODEL_WORKER_MODEL or MODEL_NAME."
}

function Get-EnvValue {
  param(
    [string]$Path,
    [string]$Key
  )
  $line = Get-Content $Path | Where-Object { $_ -match "^[\\s]*$Key=" } | Select-Object -First 1
  if (-not $line) {
    return $null
  }
  return ($line -split "=", 2)[1].Trim()
}

$gpuPort = Get-EnvValue -Path $EnvFile -Key "GPU_WORKER_HTTP_PORT"
$healthPort = Get-EnvValue -Path $EnvFile -Key "MODEL_HEALTH_PORT"
if (-not $gpuPort -or -not $healthPort) {
  throw "GPU_WORKER_HTTP_PORT or MODEL_HEALTH_PORT not found in $EnvFile"
}

try {
  docker compose -f $ComposeFile -f $ComposeOverride --env-file $EnvFile up -d

$healthUrl = "$($HostBaseUrl.TrimEnd('/')):$healthPort/health"
  $deadline = (Get-Date).AddSeconds($StartupWaitSeconds)
  $ready = $false
  while ((Get-Date) -lt $deadline) {
    try {
      $resp = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -Method Get -TimeoutSec 5
      if ($resp.StatusCode -eq 200) {
        $ready = $true
        break
      }
    } catch {
    }
    Start-Sleep -Seconds 2
  }

  if (-not $ready) {
    throw "Model worker health check did not succeed within $StartupWaitSeconds seconds."
  }

  $env:CWI_MODEL_WORKER_URL = "$($HostBaseUrl.TrimEnd('/')):$gpuPort"
  $env:CWI_MODEL_WORKER_HEALTH_URL = "$($HostBaseUrl.TrimEnd('/')):$healthPort"
  $env:CWI_MODEL_WORKER_MODEL = $Model

  python -m pytest tools/cwi-gold-tests/tests/test_model_worker_smoke.py -q
}
finally {
  docker compose -f $ComposeFile -f $ComposeOverride --env-file $EnvFile down
}
