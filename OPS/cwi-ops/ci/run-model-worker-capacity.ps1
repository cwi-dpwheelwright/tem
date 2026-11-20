param(
  [string]$BaseUrl = $env:CWI_MODEL_WORKER_URL,
  [string]$Model = $env:CWI_MODEL_WORKER_MODEL,
  [int]$DurationSeconds = 30,
  [int]$Concurrency = 4,
  [string]$OutputDir = "artifacts/model-worker-capacity"
)

$ErrorActionPreference = "Stop"

if (-not $BaseUrl) {
  throw "BaseUrl is required. Set CWI_MODEL_WORKER_URL or pass -BaseUrl."
}
if (-not $Model) {
  $Model = $env:MODEL_NAME
}
if (-not $Model) {
  throw "Model is required. Set CWI_MODEL_WORKER_MODEL or MODEL_NAME."
}

python -m pip install --upgrade httpx | Out-Host
python OPS/cwi-ops/scripts/model-worker-capacity.py `
  --base-url $BaseUrl `
  --model $Model `
  --duration-seconds $DurationSeconds `
  --concurrency $Concurrency `
  --output-dir $OutputDir
