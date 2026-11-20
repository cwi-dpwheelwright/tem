param(
  [string]$BaseUrl = $env:CWI_BASE_URL,
  [string]$Suite = "integration",
  [string]$VectorFixture = "",
  [string]$VectorCollection = "",
  [int]$VectorDimension = 0,
  [string[]]$VectorShardUrl = @(),
  [string]$JsonSummary = "artifacts/teacher-node/gold-summary.json"
)

$ErrorActionPreference = "Stop"

if (-not $BaseUrl) {
  Write-Host "CWI_BASE_URL (or -BaseUrl) is required."
  exit 1
}

if ($VectorFixture) {
  $args = @("tools/teacher-node/teacher_node.py", "eval-setup", "--vector-fixture", $VectorFixture)
  if ($VectorCollection) {
    $args += @("--collection", $VectorCollection)
  }
  if ($VectorDimension -gt 0) {
    $args += @("--dimension", $VectorDimension)
  }
  foreach ($shard in $VectorShardUrl) {
    $args += @("--shard-url", $shard)
  }
  python @args
}

Write-Host "Installing gold test dependencies..."
python -m pip install -e tools/cwi-gold-tests

Write-Host "Running gold tests ($Suite)..."
python tools/cwi-gold-tests/run_tests.py --suite $Suite --base-url $BaseUrl --json-summary $JsonSummary
