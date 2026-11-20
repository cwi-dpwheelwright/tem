param(
  [string]$ComposePath = "OPS/cwi-ops/compose",
  [string]$DeployPath = "OPS/cwi-ops/DEPLOY"
)

$ErrorActionPreference = "Stop"

function Invoke-SecretScan {
  param([string]$TargetPath)

  $pythonOk = $false
  try {
    $null = & python --version 2>$null
    $pythonOk = $true
  } catch {
    $pythonOk = $false
  }

  if ($pythonOk) {
    python OPS/cwi-ops/scripts/scan-secrets.py --path $TargetPath
  } else {
    . OPS/cwi-ops/scripts/scan-secrets.ps1 -Path $TargetPath
  }
}

Write-Host "Scanning .env.template files for secrets (compose)..."
Invoke-SecretScan -TargetPath $ComposePath

if (Test-Path $DeployPath) {
  Write-Host "Scanning .env.template files for secrets (deploy)..."
  Invoke-SecretScan -TargetPath $DeployPath
} else {
  Write-Host "Deploy path not found: $DeployPath"
}
