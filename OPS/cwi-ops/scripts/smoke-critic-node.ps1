[CmdletBinding()]
param(
    [string]$BaseUrl,
    [string]$OutputRoot,
    [int]$TimeoutSec = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\..")).Path
if ([string]::IsNullOrWhiteSpace($BaseUrl)) {
    $BaseUrl = if ($env:CWI_CRITIC_BASE_URL) { $env:CWI_CRITIC_BASE_URL } else { "http://127.0.0.1:8086" }
}

$BaseUrl = $BaseUrl.TrimEnd("/")
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $RepoRoot "testing\runs\critic-node"
}
elseif ($OutputRoot -notmatch '^[a-zA-Z]:\\|^\\\\') {
    $OutputRoot = Join-Path $RepoRoot $OutputRoot
}

$RunId = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$RunRoot = Join-Path $OutputRoot $RunId
$HealthPath = Join-Path $RunRoot "health.json"
$RequestPath = Join-Path $RunRoot "observe-request.json"
$ResponsePath = Join-Path $RunRoot "observe-response.json"
$ValidationPath = Join-Path $RunRoot "validation.json"
$ReportPath = Join-Path $RunRoot "report.md"

function New-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force -ErrorAction Stop | Out-Null
    }

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Failed to create directory: $Path"
    }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )
    $json = $Value | ConvertTo-Json -Depth 100
    $json | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Write-TextFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Value
    )
    $Value | Set-Content -LiteralPath $Path -Encoding UTF8
}

New-Directory -Path $RunRoot

$checks = @()
function Add-Check {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$Passed,
        [string]$Detail = ""
    )
    $script:checks += [pscustomobject]@{
        name = $Name
        passed = $Passed
        detail = $Detail
    }
}

$health = $null
$observe = $null
$payload = [ordered]@{
    schema_version = "runtime-critic-observation.v1"
    tenant_id = "smoke-tenant"
    user_id_hash = "smoke-user-hash"
    request_id = "critic-node-smoke"
    trace_id = "critic-node-smoke"
    context = [ordered]@{
        route = "model_answer"
        intent_type = "practical_guidance"
        query_classification = "general_reasoning"
        presentation_template_family = "practical_guidance"
        chat_response_profile = "chat_practical_guidance"
        evidence_required = $false
        trusted_web_context_requested = $false
        trusted_web_context_enabled = $false
        home_node_id_present = $false
        counts = [ordered]@{
            memory_results = 0
            source_results = 0
            graph_evidence = 0
            retrieved = 0
            home_node_sources = 0
            trusted_web_results = 0
        }
    }
    response = [ordered]@{
        answer = "I do not have enough governed evidence to answer this everyday question."
        confidence = 0.5
        execution_state = "completed"
        execution_target = "shared_compute"
        source_count = 0
        trace = [ordered]@{
            route = "model_answer"
            policy_mode = "observe"
            model = "smoke-model"
            pack_hash = "smoke-pack-hash"
            trace_id = "critic-node-smoke"
        }
    }
}

try {
    $health = Invoke-RestMethod -Uri "$BaseUrl/health" -Method Get -TimeoutSec $TimeoutSec
    Add-Check -Name "health_endpoint_reachable" -Passed $true -Detail "$BaseUrl/health"
    Add-Check -Name "health_status_healthy" -Passed ($health.status -eq "healthy") -Detail "status=$($health.status)"
    Add-Check -Name "health_service_identity" -Passed ($health.service -eq "cwi-critic-node") -Detail "service=$($health.service)"
}
catch {
    Add-Check -Name "health_endpoint_reachable" -Passed $false -Detail $_.Exception.Message
}

Write-JsonFile -Path $HealthPath -Value ([ordered]@{ base_url = $BaseUrl; health = $health })
Write-JsonFile -Path $RequestPath -Value $payload

try {
    $observe = Invoke-RestMethod `
        -Uri "$BaseUrl/runtime/critic/observe" `
        -Method Post `
        -ContentType "application/json" `
        -Body ($payload | ConvertTo-Json -Depth 100) `
        -TimeoutSec $TimeoutSec

    Add-Check -Name "observe_endpoint_reachable" -Passed $true -Detail "$BaseUrl/runtime/critic/observe"
    Add-Check -Name "observe_mode_is_observe_only" -Passed ($observe.mode -eq "observe_only") -Detail "mode=$($observe.mode)"
    $scoreCount = if ($null -ne $observe.scores) { @($observe.scores).Count } else { 0 }
    $findingCount = if ($null -ne $observe.findings) { @($observe.findings).Count } else { 0 }
    Add-Check -Name "observe_has_scores" -Passed ($scoreCount -gt 0) -Detail "score_count=$scoreCount"
    Add-Check -Name "observe_flags_known_backend_language" -Passed (
        $null -ne $observe.findings -and
        @($observe.findings | Where-Object { $_.rule -eq "backend_language_in_everyday_answer" }).Count -gt 0
    ) -Detail "finding_count=$findingCount"
}
catch {
    Add-Check -Name "observe_endpoint_reachable" -Passed $false -Detail $_.Exception.Message
}

Write-JsonFile -Path $ResponsePath -Value ([ordered]@{ observe = $observe })

$failed = @($checks | Where-Object { -not $_.passed })
$validation = [ordered]@{
    run_id = $RunId
    base_url = $BaseUrl
    passed = ($failed.Count -eq 0)
    failed_count = $failed.Count
    checks = $checks
}
Write-JsonFile -Path $ValidationPath -Value $validation

$lines = @()
$lines += "# Critic Node Smoke Test"
$lines += ""
$lines += "- Run ID: ``$RunId``"
$lines += "- Base URL: ``$BaseUrl``"
$resultText = if ($failed.Count -eq 0) { "PASS" } else { "FAIL" }
$lines += "- Result: $resultText"
$lines += ""
$lines += "| Check | Result | Detail |"
$lines += "|---|---:|---|"
foreach ($check in $checks) {
    $result = if ($check.passed) { "PASS" } else { "FAIL" }
    $detail = ($check.detail -replace "\|", "/")
    $lines += "| $($check.name) | $result | $detail |"
}
Write-TextFile -Path $ReportPath -Value ($lines -join "`r`n")

if ($failed.Count -gt 0) {
    Write-Error "Critic node smoke failed. Report: $ReportPath"
}

Write-Host "Critic node smoke passed. Report: $ReportPath"
