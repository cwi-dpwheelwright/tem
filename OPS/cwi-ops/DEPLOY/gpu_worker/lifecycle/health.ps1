param(
  [string]$EnvFile = "OPS/cwi-ops/DEPLOY/gpu_worker/.env",
  [string]$Host = "localhost",
  [switch]$SkipMetrics
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $EnvFile)) {
  throw "Env file not found: $EnvFile"
}

function Get-EnvValue {
  param(
    [string]$Path,
    [string]$Key
  )
  $line = Get-Content $Path | Where-Object { $_ -match "^[\\s]*$Key=" } | Select-Object -First 1
  if (-not $line) {
    return $null
  }
  return ($line -split "=", 2)[1].Trim()
}

function Invoke-StatusCheck {
  param(
    [string]$Url,
    [int]$ExpectedStatus
  )

  try {
    $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -Method Get -TimeoutSec 10
    $actual = $response.StatusCode
  } catch {
    if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
      $actual = [int]$_.Exception.Response.StatusCode
    } else {
      throw
    }
  }

  if ($actual -ne $ExpectedStatus) {
    throw "Expected $ExpectedStatus for $Url but got $actual"
  }

  Write-Host "OK $Url -> $actual"
}

$ports = @(
  (Get-EnvValue -Path $EnvFile -Key "MODEL_HEALTH_PORT"),
  (Get-EnvValue -Path $EnvFile -Key "MODEL_HEALTH_PRO_PORT"),
  (Get-EnvValue -Path $EnvFile -Key "MODEL_HEALTH_FREE_PORT")
) | Where-Object { $_ }

if ($ports.Count -eq 0) {
  throw "MODEL_HEALTH_PORT values not found in $EnvFile"
}

foreach ($port in $ports) {
  $base = "http://$Host`:$port"
  Invoke-StatusCheck -Url "$base/health" -ExpectedStatus 200
  if (-not $SkipMetrics) {
    Invoke-StatusCheck -Url "$base/metrics" -ExpectedStatus 200
  }
}
