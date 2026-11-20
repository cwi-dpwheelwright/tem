param(
  [string]$BaseUrl = "http://localhost:8081",
  [int]$ExpectedReadyStatus = 503
)

$ErrorActionPreference = "Stop"

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
    Write-Host "Expected $ExpectedStatus for $Url but got $actual"
    exit 1
  }

  Write-Host "OK $Url -> $actual"
}

Invoke-StatusCheck -Url "$BaseUrl/api/health" -ExpectedStatus 200
Invoke-StatusCheck -Url "$BaseUrl/api/ready" -ExpectedStatus $ExpectedReadyStatus
