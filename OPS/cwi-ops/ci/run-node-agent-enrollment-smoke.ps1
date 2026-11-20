$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$artifactDir = Join-Path $repoRoot "artifacts\qa"
if (-not (Test-Path $artifactDir)) {
  New-Item -ItemType Directory -Path $artifactDir | Out-Null
}

$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$artifactPath = Join-Path $artifactDir "iteration-08-node-agent-$timestamp.txt"

$scriptPath = Join-Path $repoRoot "OPS\cwi-ops\scripts\node-agent-enrollment-smoke.py"

$python = $env:PYTHON
if (-not $python) {
  $python = "python"
}

& $python $scriptPath --repo-root $repoRoot 2>&1 | Tee-Object -FilePath $artifactPath

Write-Host "wrote $artifactPath"
