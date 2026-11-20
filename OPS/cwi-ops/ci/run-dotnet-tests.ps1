param(
  [string]$SolutionRoot = "src/backend/services",
  [string]$ResultsDir = "artifacts/test-results/dotnet"
)

$ErrorActionPreference = "Stop"

$resultsPath = Resolve-Path -Path $ResultsDir -ErrorAction SilentlyContinue
if (-not $resultsPath) {
  New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
  $resultsPath = Resolve-Path -Path $ResultsDir
}

$trxConverter = Get-Command trx2junit -ErrorAction SilentlyContinue
if (-not $trxConverter) {
  Write-Host "trx2junit not found. TRX files will be produced without conversion."
}

$solutions = @(
  "cwi-router/cwi-router.sln",
  "cwi-orchestrator/cwi-orchestrator.sln",
  "cwi-coordination-layer/cwi-coordination-layer.sln",
  "cwi-identity-server/cwi-identity-server.sln"
)

foreach ($solution in $solutions) {
  $path = Join-Path $SolutionRoot $solution
  $solutionName = ($solution -replace '[\\/]', '_') -replace '\.sln$', ''
  $trxName = "$solutionName.trx"
  Write-Host "Running dotnet test for $path"
  dotnet test $path -c Release --logger "trx;LogFileName=$trxName" --results-directory $resultsPath
  if ($trxConverter) {
    $trxPath = Join-Path $resultsPath $trxName
    & $trxConverter $trxPath | Out-Host
  }
}
