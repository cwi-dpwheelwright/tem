param(
  [string]$BaseUrl = "http://localhost:6333",
  [string]$Collection = $env:VECTOR_COLLECTION,
  [switch]$RequireCollection
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

function Invoke-HealthCheck {
  param([string]$Url)
  try {
    Invoke-StatusCheck -Url "$Url/healthz" -ExpectedStatus 200
    return
  } catch {
    Invoke-StatusCheck -Url "$Url/health" -ExpectedStatus 200
  }
}

Invoke-HealthCheck -Url $BaseUrl
Invoke-StatusCheck -Url "$BaseUrl/collections" -ExpectedStatus 200

if ($Collection) {
  try {
    Invoke-StatusCheck -Url "$BaseUrl/collections/$Collection" -ExpectedStatus 200
  } catch {
    if ($RequireCollection) {
      throw
    }
    Write-Host "Collection $Collection not found (expected in non-required mode)."
  }
}
