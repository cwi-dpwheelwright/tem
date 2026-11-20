$ErrorActionPreference = "Stop"

function New-GateRun {
  param(
    [Parameter(Mandatory = $true)][string]$Name,
    [string]$OutputRoot = "artifacts/gates"
  )

  $timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
  $safeName = ($Name -replace "[^a-zA-Z0-9_.-]+", "-").Trim("-")
  if (-not $safeName) { $safeName = "gate" }
  $outputDir = Join-Path $OutputRoot "$safeName-$timestamp"

  New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

  return [pscustomobject]@{
    Name = $Name
    SafeName = $safeName
    TimestampUtc = $timestamp
    OutputDir = $outputDir
    Results = @()
    HadFailure = $false
  }
}

function Invoke-GateStep {
  param(
    [Parameter(Mandatory = $true)][pscustomobject]$Gate,
    [Parameter(Mandatory = $true)][string]$Name,
    [Parameter(Mandatory = $true)][scriptblock]$Action,
    [string]$LogPath,
    [switch]$AllowFailure
  )

  $stepSafe = ($Name -replace "[^a-zA-Z0-9_.-]+", "-").Trim("-")
  if (-not $stepSafe) { $stepSafe = "step" }
  if (-not $LogPath) {
    $LogPath = Join-Path $Gate.OutputDir "$stepSafe.log"
  }

  Write-Host ""
  Write-Host "== $Name =="

  $started = Get-Date
  $status = "PASS"
  $error = $null
  $exitCode = $null

  try {
    & $Action 2>&1 | Tee-Object -FilePath $LogPath -Append | Out-Host
    if ($LASTEXITCODE -ne $null) { $exitCode = [int]$LASTEXITCODE }
    if ($exitCode -ne $null -and $exitCode -ne 0) {
      throw "Exit code $exitCode"
    }
  } catch {
    $status = "FAIL"
    $error = $_.Exception.Message
    if (-not $AllowFailure) { $Gate.HadFailure = $true }
    Write-Warning "$Name failed: $error"
  }

  $duration = (New-TimeSpan -Start $started -End (Get-Date)).TotalSeconds
  $Gate.Results += [pscustomobject]@{
    step = $Name
    status = $status
    error = $error
    log = $LogPath
    duration_seconds = [Math]::Round($duration, 2)
    exit_code = $exitCode
    allowed_failure = [bool]$AllowFailure
    started_utc = $started.ToUniversalTime().ToString("o")
  }
}

function Write-GateReport {
  param(
    [Parameter(Mandatory = $true)][pscustomobject]$Gate,
    [string]$ReportName = "gate-report.json"
  )

  $reportPath = Join-Path $Gate.OutputDir $ReportName
  $payload = [pscustomobject]@{
    gate = $Gate.Name
    safe_name = $Gate.SafeName
    timestamp_utc = $Gate.TimestampUtc
    output_dir = $Gate.OutputDir
    had_failure = $Gate.HadFailure
    steps = $Gate.Results
  }

  $payload | ConvertTo-Json -Depth 6 | Out-File -FilePath $reportPath -Encoding utf8
  Write-Host ""
  Write-Host "Report written to $reportPath"
  return $reportPath
}

function Write-GateSummary {
  param([Parameter(Mandatory = $true)][pscustomobject]$Gate)
  Write-Host ""
  Write-Host "Summary:"
  $Gate.Results | Format-Table -AutoSize
}
