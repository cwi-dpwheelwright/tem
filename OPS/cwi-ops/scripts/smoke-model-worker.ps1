param(
  [string]$BaseUrl = $env:CWI_MODEL_WORKER_URL,
  [string]$HealthUrl = $env:CWI_MODEL_WORKER_HEALTH_URL,
  [string]$Model = $env:CWI_MODEL_WORKER_MODEL,
  [string]$Prompt = "Return the word 'ready'.",
  [switch]$SkipMetrics
)

$ErrorActionPreference = "Stop"

if (-not $BaseUrl) {
  $BaseUrl = $env:MODEL_BASE_URL
}
if (-not $Model) {
  $Model = $env:CWI_MODEL_ID
}
if (-not $Model) {
  $Model = $env:MODEL_NAME
}

if (-not $BaseUrl) {
  throw "BaseUrl is required. Set CWI_MODEL_WORKER_URL or MODEL_BASE_URL."
}
if (-not $Model) {
  throw "Model is required. Set CWI_MODEL_WORKER_MODEL, CWI_MODEL_ID, or MODEL_NAME."
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

if ($HealthUrl) {
  Invoke-StatusCheck -Url "$($HealthUrl.TrimEnd('/'))/health" -ExpectedStatus 200
  if (-not $SkipMetrics) {
    Invoke-StatusCheck -Url "$($HealthUrl.TrimEnd('/'))/metrics" -ExpectedStatus 200
  }
} else {
  Write-Host "HealthUrl not set; skipping /health and /metrics checks."
}

$payload = @{ model = $Model; prompt = $Prompt; stream = $false } | ConvertTo-Json
$headers = @{ "Content-Type" = "application/json" }
$response = Invoke-RestMethod -Method Post -Uri "$($BaseUrl.TrimEnd('/'))/api/generate" -Body $payload -Headers $headers
if (-not $response.response) {
  throw "Expected response text from /api/generate."
}
Write-Host "OK /api/generate -> response length $($response.response.Length)"
