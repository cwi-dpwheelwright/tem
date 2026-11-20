param(
  [string]$BaseUrl = $env:CWI_ORCHESTRATOR_URL,
  [int]$TraceLimit = 25,
  [string[]]$TraceIds = @(),
  [string]$OutputDir = "artifacts/ledger-verify"
)

$ErrorActionPreference = "Stop"

if (-not $BaseUrl) {
  throw "BaseUrl is required. Set CWI_ORCHESTRATOR_URL."
}

$headers = @{}
if ($env:CWI_AUTH_TOKEN) {
  $headers["Authorization"] = "Bearer $($env:CWI_AUTH_TOKEN)"
}

$traceIds = @()
if ($TraceIds.Count -gt 0) {
  $traceIds = $TraceIds
} elseif ($env:CWI_TRACE_IDS) {
  $traceIds = $env:CWI_TRACE_IDS.Split(",") | ForEach-Object { $_.Trim() } | Where-Object { $_ }
} else {
  $eventsUrl = "$($BaseUrl.TrimEnd('/'))/api/ledger/events?page=1&page_size=$TraceLimit"
  Write-Host "Fetching ledger events from $eventsUrl"
  try {
    $events = Invoke-RestMethod -Uri $eventsUrl -Headers $headers -Method Get
    foreach ($item in $events.items) {
      if ($item.trace_id -and -not ($traceIds -contains $item.trace_id)) {
        $traceIds += $item.trace_id
      }
    }
  } catch {
    Write-Warning "Failed to fetch ledger events: $($_.Exception.Message)"
  }
}

if ($traceIds.Count -eq 0) {
  throw "No trace IDs available for verification. Provide -TraceIds or set CWI_TRACE_IDS."
}

$results = @()
foreach ($traceId in $traceIds) {
  $verifyUrl = "$($BaseUrl.TrimEnd('/'))/api/ledger/verify?trace_id=$traceId"
  try {
    $result = Invoke-RestMethod -Uri $verifyUrl -Headers $headers -Method Get
    $ok = $null
    $reason = $null
    if ($result.PSObject.Properties.Match("ok").Count -gt 0) {
      $ok = $result.ok
      $reason = $result.reason
    } elseif ($result.PSObject.Properties.Match("isValid").Count -gt 0) {
      $ok = $result.isValid
      $reason = $result.error
    }
    if ($null -eq $ok) {
      $ok = $false
      if (-not $reason) { $reason = "Ledger verify response missing ok/isValid." }
    }
    $results += [pscustomobject]@{
      trace_id = $traceId
      ok = $ok
      reason = $reason
    }
  } catch {
    $results += [pscustomobject]@{
      trace_id = $traceId
      ok = $false
      reason = $_.Exception.Message
    }
  }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
$outPath = Join-Path $OutputDir "ledger-verify-$timestamp.json"
$results | ConvertTo-Json -Depth 3 | Out-File -FilePath $outPath -Encoding utf8
Write-Host "Wrote $outPath"

$failed = $results | Where-Object { -not $_.ok }
if ($failed.Count -gt 0) {
  Write-Error "Ledger verification failed for $($failed.Count) trace(s)."
  exit 2
}

Write-Host "Ledger verification passed."
