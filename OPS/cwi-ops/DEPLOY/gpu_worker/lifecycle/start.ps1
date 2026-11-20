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
  [switch]$Attach
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ComposeFile)) {
  throw "Compose file not found: $ComposeFile"
}
if (-not (Test-Path $EnvFile)) {
  throw "Env file not found: $EnvFile"
}

$composeArgs = @("-f", $ComposeFile, "--env-file", $EnvFile)

if ($Attach) {
  docker compose @composeArgs up @Services
} else {
  docker compose @composeArgs up -d @Services
}
