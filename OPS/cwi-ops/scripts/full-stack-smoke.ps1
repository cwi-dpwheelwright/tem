param(
  [string]$GatewayBaseUrl = "http://localhost:18081",
  [string]$IdentityBaseUrl = "http://localhost:8085",
  [string]$PackServiceBaseUrl = "http://localhost:18083",
  [string]$TeamsBotBaseUrl = "http://localhost:13978",
  [string]$NodeUiBaseUrl = "http://localhost:18090",
  [switch]$SkipGateway,
  [switch]$SkipIdentity,
  [switch]$SkipPackService,
  [switch]$SkipTeamsBot,
  [switch]$SkipNodeUi
)

$ErrorActionPreference = "Stop"

function Normalize-BaseUrl {
  param([string]$Url)
  if (-not $Url) {
    return ""
  }
  return $Url.TrimEnd("/")
}

function Invoke-StatusCheck {
  param(
    [string]$Url,
    [int]$ExpectedStatus = 200
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

function Invoke-AnyStatusCheck {
  param(
    [string]$BaseUrl,
    [string[]]$Paths,
    [int]$ExpectedStatus = 200
  )

  $lastError = $null
  foreach ($path in $Paths) {
    try {
      Invoke-StatusCheck -Url "$BaseUrl$path" -ExpectedStatus $ExpectedStatus
      return
    } catch {
      $lastError = $_
    }
  }

  if ($lastError) {
    throw $lastError
  }
  throw "No health paths provided."
}

$gateway = Normalize-BaseUrl $GatewayBaseUrl
$identity = Normalize-BaseUrl $IdentityBaseUrl
$packs = Normalize-BaseUrl $PackServiceBaseUrl
$teams = Normalize-BaseUrl $TeamsBotBaseUrl
$nodeUi = Normalize-BaseUrl $NodeUiBaseUrl

if (-not $SkipGateway) {
  Write-Host "== gateway =="
  Invoke-StatusCheck -Url "$gateway/api/health" -ExpectedStatus 200
  Invoke-StatusCheck -Url "$gateway/api/ready" -ExpectedStatus 200
}

if (-not $SkipIdentity) {
  Write-Host "== identity =="
  Invoke-AnyStatusCheck -BaseUrl $identity -Paths @("/healthz", "/health") -ExpectedStatus 200
}

if (-not $SkipPackService) {
  Write-Host "== pack service =="
  Invoke-StatusCheck -Url "$packs/packs/index.json" -ExpectedStatus 200
}

if (-not $SkipTeamsBot) {
  Write-Host "== teams bot =="
  Invoke-StatusCheck -Url "$teams/health" -ExpectedStatus 200
}

if (-not $SkipNodeUi) {
  Write-Host "== node ui =="
  Invoke-StatusCheck -Url "$nodeUi/" -ExpectedStatus 200
}

Write-Host "Full-stack smoke checks completed."
