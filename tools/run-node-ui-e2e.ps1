param(
  [string]$EnvFile = "OPS/cwi-ops/compose/full/.env",
  [string]$BaseUrl = "http://localhost:3000",
  [int]$WaitSeconds = 60
)

$ErrorActionPreference = "Stop"

function Load-EnvFile {
  param([string]$Path)
  if (-not (Test-Path $Path)) {
    throw "Env file not found: $Path"
  }
  Get-Content $Path | ForEach-Object {
    $line = $_.Trim()
    if (-not $line -or $line.StartsWith("#")) {
      return
    }
    $parts = $line -split "=", 2
    if ($parts.Count -eq 2) {
      Set-Item -Path "Env:$($parts[0])" -Value $parts[1]
    }
  }
}

Load-EnvFile -Path $EnvFile
$env:PORT = "3000"
$env:PLAYWRIGHT_BASE_URL = $BaseUrl
if ($env:NEXT_PUBLIC_PACKS_BASE_URL) {
  $env:PLAYWRIGHT_PACKS_BASE_URL = $env:NEXT_PUBLIC_PACKS_BASE_URL
}


$root = Split-Path -Parent $PSScriptRoot
$qaDir = Join-Path $root "artifacts/qa"
New-Item -ItemType Directory -Force -Path $qaDir | Out-Null
$ts = Get-Date -Format "yyyyMMdd-HHmmss"
$serverLog = Join-Path $qaDir "iteration-08-node-ui-start-$ts.txt"
$serverErr = Join-Path $qaDir "iteration-08-node-ui-start-$ts.err.txt"
$e2eLog = Join-Path $qaDir "iteration-08-node-ui-e2e-$ts.txt"

$server = Start-Process -FilePath "npm.cmd" -ArgumentList @("run", "start") -PassThru -NoNewWindow -RedirectStandardOutput $serverLog -RedirectStandardError $serverErr
try {
  $deadline = (Get-Date).AddSeconds($WaitSeconds)
  do {
    try {
      $resp = Invoke-WebRequest -Uri "$BaseUrl/api/health" -UseBasicParsing -TimeoutSec 3
      if ($resp.StatusCode -eq 200) { break }
    } catch {
    }
    Start-Sleep -Seconds 2
  } while ((Get-Date) -lt $deadline)

  npm run test:e2e *>&1 | Tee-Object -FilePath $e2eLog
} finally {
  if ($server -and -not $server.HasExited) {
    Stop-Process -Id $server.Id -Force
  }
}
