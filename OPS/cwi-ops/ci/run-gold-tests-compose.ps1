param(
  [string]$ComposeFile = "OPS/cwi-ops/compose/test/docker-compose.test.yml",
  [string]$EnvFile = "OPS/cwi-ops/compose/test/.env",
  [string]$Profile = "smoke",
  [string]$BaseUrl = "http://localhost:18081",
  [int]$StartupWaitSeconds = 60,
  [string]$CoordinationUrl = "",
  [string]$CoordinationAdminToken = "",
  [string]$CoordinationAdminHeader = "",
  [string]$CoordinationRequireAdmin = ""
)

$ErrorActionPreference = "Stop"

function Import-EnvFile {
  param([string]$Path)

  if (-not (Test-Path $Path)) {
    throw "Env file not found: $Path"
  }

  $root = (Get-Item -Path ".").FullName
  Get-Content -LiteralPath $Path | ForEach-Object {
    $line = $_.Trim()
    if (-not $line -or $line.StartsWith("#")) {
      return
    }
    if ($line -notmatch "=") {
      return
    }
    $parts = $line -split "=", 2
    $name = $parts[0].Trim()
    $value = $parts[1]
    if ($value.StartsWith('"') -and $value.EndsWith('"')) {
      $value = $value.Substring(1, $value.Length - 2)
    }
    if ($name -eq "CWI_MODEL_WORKER_LIFECYCLE_DIR" -and -not [IO.Path]::IsPathRooted($value)) {
      $value = Join-Path $root $value
    }
    if ($name) {
      Set-Item -Path "Env:$name" -Value $value
    }
  }
}

if (-not (Test-Path $ComposeFile)) {
  Write-Host "Compose file not found: $ComposeFile"
  exit 1
}

if (-not (Test-Path $EnvFile)) {
  Write-Host "Env file not found: $EnvFile"
  exit 1
}

Import-EnvFile -Path $EnvFile

if (-not $CoordinationUrl) {
  $CoordinationUrl = $env:CWI_COORDINATION_URL
}
if (-not $CoordinationAdminToken) {
  $CoordinationAdminToken = $env:CWI_COORDINATION_ADMIN_TOKEN
}
if (-not $CoordinationAdminHeader) {
  $CoordinationAdminHeader = $env:CWI_COORDINATION_ADMIN_HEADER
}
if (-not $CoordinationRequireAdmin) {
  $CoordinationRequireAdmin = $env:CWI_COORDINATION_REQUIRE_ADMIN
}

try {
  docker compose -f $ComposeFile --env-file $EnvFile --profile $Profile up -d

  Write-Host "Waiting for router health..."
  $deadline = (Get-Date).AddSeconds($StartupWaitSeconds)
  $ready = $false
  while ((Get-Date) -lt $deadline) {
    try {
      $health = Invoke-RestMethod -TimeoutSec 2 -Uri "$BaseUrl/api/health"
      if ($health.ok -eq $true) {
        $ready = $true
        break
      }
    } catch {
    }
    Start-Sleep -Seconds 2
  }

  if (-not $ready) {
    Write-Host "Router health check did not succeed within $StartupWaitSeconds seconds."
    exit 1
  }

  if ($CoordinationUrl) {
    Write-Host "Waiting for coordination-layer health..."
    $coordDeadline = (Get-Date).AddSeconds($StartupWaitSeconds)
    $coordReady = $false
    while ((Get-Date) -lt $coordDeadline) {
      try {
        $coordHealth = Invoke-RestMethod -TimeoutSec 2 -Uri "$CoordinationUrl/health"
        if ($coordHealth.status -eq "ok") {
          $coordReady = $true
          break
        }
      } catch {
      }
      Start-Sleep -Seconds 2
    }

    if (-not $coordReady) {
      Write-Host "Coordination-layer health check did not succeed within $StartupWaitSeconds seconds."
      exit 1
    }
  }

  $runner = Join-Path "OPS/cwi-ops/ci" "run-gold-tests.ps1"
  if (-not (Test-Path $runner)) {
    Write-Host "Gold tests runner not found at $runner"
    exit 1
  }

  & $runner -BaseUrl $BaseUrl `
    -CoordinationUrl $CoordinationUrl `
    -CoordinationAdminToken $CoordinationAdminToken `
    -CoordinationAdminHeader $CoordinationAdminHeader `
    -CoordinationRequireAdmin $CoordinationRequireAdmin
}
finally {
  docker compose -f $ComposeFile --env-file $EnvFile --profile $Profile down
}
