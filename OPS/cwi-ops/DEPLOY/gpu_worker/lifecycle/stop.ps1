param(
  [string]$ComposeFile = "OPS/cwi-ops/DEPLOY/gpu_worker/docker-compose.yml",
  [string]$EnvFile = "OPS/cwi-ops/DEPLOY/gpu_worker/.env",
  [string[]]$Services = @(
    "cwi-gpu-worker",
    "cwi-gpu-worker-pro",
    "cwi-gpu-worker-free",
    "cwi-model-worker-health",
    "cwi-model-worker-health-pro",
    "cwi-model-worker-health-free"
  ),
  [switch]$Down
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ComposeFile)) {
  throw "Compose file not found: $ComposeFile"
}
if (-not (Test-Path $EnvFile)) {
  throw "Env file not found: $EnvFile"
}

$composeArgs = @("-f", $ComposeFile, "--env-file", $EnvFile)

if ($Down) {
  docker compose @composeArgs down
} else {
  docker compose @composeArgs stop @Services
}
