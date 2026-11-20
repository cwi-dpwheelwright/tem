param(
  [string]$BaseUrl = $env:PERF_BASE_URL,
  [int]$DurationSeconds = 30,
  [int]$Concurrency = 4,
  [string]$PackName = "enterprise",
  [string]$OutputDir = "artifacts/perf-smoke"
)

$ErrorActionPreference = "Stop"

if (-not $BaseUrl) {
  throw "BaseUrl is required. Set PERF_BASE_URL or pass -BaseUrl."
}

python -m pip install --upgrade httpx | Out-Host
python OPS/cwi-ops/scripts/perf-smoke.py `
  --base-url $BaseUrl `
  --duration-seconds $DurationSeconds `
  --concurrency $Concurrency `
  --pack-name $PackName `
  --output-dir $OutputDir
