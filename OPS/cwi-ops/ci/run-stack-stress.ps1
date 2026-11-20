param(
  [string]$BaseUrl = $env:CWI_BASE_URL,
  [int]$StartConcurrency = 4,
  [int]$StepConcurrency = 4,
  [int]$MaxConcurrency = 64,
  [int]$StepDurationSeconds = 60,
  [int]$CooldownSeconds = 10,
  [double]$TimeoutSeconds = 30,
  [string]$PackName = "enterprise",
  [string]$LogMode = "all",
  [string]$OutputDir = "artifacts/stack-stress",
  [double]$MaxErrorRate = 0.05,
  [double]$MaxP95Ms = 8000,
  [double]$MaxP99Ms = 12000,
  [double]$MaxAvgMs = 5000,
  [switch]$NoAction,
  [switch]$NoAsk,
  [switch]$NoStopOnThreshold
)

$ErrorActionPreference = "Stop"

if (-not $BaseUrl) {
  throw "BaseUrl is required. Set CWI_BASE_URL or pass -BaseUrl."
}

python -m pip install --upgrade httpx | Out-Host

$argsList = @(
  "OPS/cwi-ops/scripts/stack-stress.py",
  "--base-url", $BaseUrl,
  "--pack-name", $PackName,
  "--start-concurrency", $StartConcurrency,
  "--step-concurrency", $StepConcurrency,
  "--max-concurrency", $MaxConcurrency,
  "--step-duration-seconds", $StepDurationSeconds,
  "--cooldown-seconds", $CooldownSeconds,
  "--timeout-seconds", $TimeoutSeconds,
  "--log-mode", $LogMode,
  "--output-dir", $OutputDir,
  "--max-error-rate", $MaxErrorRate,
  "--max-p95-ms", $MaxP95Ms,
  "--max-p99-ms", $MaxP99Ms,
  "--max-avg-ms", $MaxAvgMs
)

if ($NoAction) {
  $argsList += "--no-action"
}
if ($NoAsk) {
  $argsList += "--no-ask"
}
if ($NoStopOnThreshold) {
  $argsList += "--no-stop-on-threshold"
}

python @argsList
