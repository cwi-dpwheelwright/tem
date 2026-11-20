param(
  [string]$BaseUrl = "http://100.124.140.66",
  [string]$TenantId = "tenant-1",
  [string]$UserId = "e2e-test",
  [string]$PackId = "",
  [int]$TimeoutSec = 20
)

$ErrorActionPreference = "Stop"

function Write-Section($title) {
  Write-Host "==> $title"
}

function Invoke-JsonRequest {
  param(
    [Parameter(Mandatory=$true)][string]$Method,
    [Parameter(Mandatory=$true)][string]$Url,
    [object]$Body = $null
  )

  $params = @{
    Method = $Method
    Uri = $Url
    TimeoutSec = $TimeoutSec
    Headers = @{ "Content-Type" = "application/json" }
    ErrorAction = "Stop"
  }

  # Windows PowerShell can throw NullReferenceException without -UseBasicParsing.
  if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey("UseBasicParsing")) {
    $params.UseBasicParsing = $true
  }

  if ($Body -ne $null) {
    $params.Body = ($Body | ConvertTo-Json -Depth 10)
  }

  $resp = Invoke-WebRequest @params
  $content = $null
  if ($resp.Content) {
    try { $content = $resp.Content | ConvertFrom-Json } catch { $content = $resp.Content }
  }

  return [pscustomobject]@{
    Status = $resp.StatusCode
    Headers = $resp.Headers
    Content = $content
    Raw = $resp.Content
  }
}

Write-Section "Preflight"
$health = Invoke-JsonRequest -Method "GET" -Url "$BaseUrl/api/health"
Write-Host "gateway /api/health => $($health.Status)"

Write-Section "Pack discovery"
$index = Invoke-JsonRequest -Method "GET" -Url "$BaseUrl/packs/index.json"
if ($index.Status -ne 200) { throw "Pack index failed: $($index.Status)" }
$packs = $index.Content.packs
if (-not $packs -or $packs.Count -eq 0) { throw "No packs found at $BaseUrl/packs/index.json" }

if ([string]::IsNullOrWhiteSpace($PackId)) {
  $PackId = $packs[0].pack_id
}
Write-Host "Using pack_id: $PackId"

$packJson = Invoke-JsonRequest -Method "GET" -Url "$BaseUrl/packs/$PackId/pack.json"
Write-Host "pack.json => $($packJson.Status)"

Write-Section "Ask"
$askPayload = @{
  tenant_id = $TenantId
  user_id = $UserId
  question = "What are the key requirements of this pack?"
  pack = @{ name = $PackId }
  context = @{ mode = "standard"; top_k = 8 }
}
$ask = Invoke-JsonRequest -Method "POST" -Url "$BaseUrl/api/ask" -Body $askPayload
Write-Host "ask => $($ask.Status)"
$askTrace = $ask.Headers["X-Trace-Id"]
if ($askTrace) { Write-Host "ask trace_id: $askTrace" }

Write-Section "Action"
$actionPayload = @{
  tenant_id = $TenantId
  user_id = $UserId
  policy_mode = "approve"
  action = @{ type = "transfer_funds"; payload = @{ amount = 100; currency = "USD" } }
}
$action = Invoke-JsonRequest -Method "POST" -Url "$BaseUrl/api/action" -Body $actionPayload
Write-Host "action => $($action.Status)"
$actionTrace = $action.Headers["X-Trace-Id"]
if ($actionTrace) { Write-Host "action trace_id: $actionTrace" }

Write-Section "Telemetry"
$telemetryPayload = @{
  tenant_id = $TenantId
  window = @{ start = "2026-02-20T00:00:00Z"; end = "2026-02-20T00:05:00Z" }
  signals = @(
    @{ tag = "cpu.util"; unit = "percent"; series = @(0.1, 0.2, 0.3) }
  )
  metadata = @{ source = "e2e" }
}
$telemetry = Invoke-JsonRequest -Method "POST" -Url "$BaseUrl/api/ingest/telemetry" -Body $telemetryPayload
Write-Host "telemetry => $($telemetry.Status)"
$telemetryTrace = $telemetry.Headers["X-Trace-Id"]
if ($telemetryTrace) { Write-Host "telemetry trace_id: $telemetryTrace" }

Write-Section "Summary"
Write-Host "gateway.health: $($health.Status)"
Write-Host "pack.index: $($index.Status)"
Write-Host "pack.json: $($packJson.Status)"
Write-Host "ask: $($ask.Status)"
Write-Host "action: $($action.Status)"
Write-Host "telemetry: $($telemetry.Status)"
