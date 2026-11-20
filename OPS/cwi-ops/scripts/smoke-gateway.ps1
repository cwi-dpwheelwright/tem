param(
  [string]$BaseUrl = "http://localhost:18081",
  [int]$ExpectedHealthStatus = 200,
  [int]$ExpectedReadyStatus = 200,
  [int]$ExpectedApiHealthStatus = 200,
  [int]$ExpectedApiReadyStatus = 200,
  [int]$TimeoutSec = 10
)

$ErrorActionPreference = "Stop"

function Invoke-StatusCheck {
  param(
    [string]$Url,
    [int]$ExpectedStatus
  )

  try {
    $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -Method Get -TimeoutSec $TimeoutSec
    $actual = $response.StatusCode
  } catch {
    if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
      $actual = [int]$_.Exception.Response.StatusCode
    } else {
      throw
    }
  }

  if ($actual -ne $ExpectedStatus) {
    Write-Host "Expected $ExpectedStatus for $Url but got $actual"
    exit 1
  }

  Write-Host "OK $Url -> $actual"
}

$base = $BaseUrl.TrimEnd("/")
Invoke-StatusCheck -Url "$base/healthz" -ExpectedStatus $ExpectedHealthStatus
Invoke-StatusCheck -Url "$base/readyz" -ExpectedStatus $ExpectedReadyStatus
Invoke-StatusCheck -Url "$base/api/health" -ExpectedStatus $ExpectedApiHealthStatus
Invoke-StatusCheck -Url "$base/api/ready" -ExpectedStatus $ExpectedApiReadyStatus
