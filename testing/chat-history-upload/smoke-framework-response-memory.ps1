[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateScript({ Test-Path -LiteralPath $_ })]
    [string]$PayloadPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$FeatureName = "chat-history-upload"
$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
$InputPath = (Resolve-Path -LiteralPath $PayloadPath).Path
$RunId = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$RunName = "$RunId-framework-response"
$RunRoot = Join-Path $RepoRoot "testing\runs\$FeatureName\$RunName"
$UploadOut = Join-Path $RunRoot "upload"
$AskOut = Join-Path $RunRoot "ask"
$ReportPath = Join-Path $RunRoot "report.md"
$AnalysisPath = Join-Path $RunRoot "analysis.json"

$PublicBaseUrl = if ($env:CWI_SMOKE_PUBLIC_BASE_URL) { $env:CWI_SMOKE_PUBLIC_BASE_URL.TrimEnd("/") } else { "https://cwigpt.com" }
$SshTarget = if ($env:CWI_SMOKE_SSH_TARGET) { $env:CWI_SMOKE_SSH_TARGET } else { "root@100.120.208.51" }
$IdentityBaseUrl = if ($env:CWI_SMOKE_IDENTITY_BASE_URL) { $env:CWI_SMOKE_IDENTITY_BASE_URL.TrimEnd("/") } else { "http://127.0.0.1:8085" }
$RemoteRoot = if ($env:CWI_SMOKE_REMOTE_ROOT) { $env:CWI_SMOKE_REMOTE_ROOT.TrimEnd("/") } else { "/tmp/cwi-smoke" }
$RemoteRunDir = "$RemoteRoot/$FeatureName/$RunName"
$BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"

$ActiveMarkers = @(
    "Project Cedar Lens",
    "OPEN-CEDAR-441",
    "CEDAR-LENS-914",
    "HOME-CEDAR-118",
    "Demitrius",
    "Chad"
)
$ExcludedMarkers = @(
    "Project Glass Finch",
    "Project Dust Archive",
    "RETRACT-CEDAR-555",
    "TANGERINE-OLD-223"
)
$HistoricalMarkers = @("Project Amber Bridge")
$ConflictMarkers = @("Riley", "Morgan", "no single truth")
$GenericFillerMarkers = @(
    "typical traits",
    "common characteristics",
    "general standpoint",
    "everyone's unique experiences",
    "open to learning new things"
)

function New-Directory {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )
    $json = $Value | ConvertTo-Json -Depth 100
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $json, $utf8NoBom)
}

function Read-JsonIfPresent {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $content = Get-Content -LiteralPath $Path -Raw
    if ([string]::IsNullOrWhiteSpace($content)) {
        return $null
    }

    try {
        return $content | ConvertFrom-Json
    }
    catch {
        return [pscustomobject]@{ raw = $content; parse_error = $_.Exception.Message }
    }
}

function Get-ObjectPropertyValue {
    param(
        $Object,
        [Parameter(Mandatory = $true)][string]$Name
    )
    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Invoke-CheckedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath failed with exit code $LASTEXITCODE."
    }
}

function Invoke-Ssh {
    param([Parameter(Mandatory = $true)][string]$Command)
    Invoke-CheckedProcess -FilePath "ssh" -Arguments @("-o", "BatchMode=yes", $SshTarget, $Command)
}

function Invoke-Scp {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    Invoke-CheckedProcess -FilePath "scp" -Arguments $Arguments
}

function Write-TempScript {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $path = Join-Path ([System.IO.Path]::GetTempPath()) $Name
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($path, $Content, $utf8NoBom)
    return $path
}

function Get-AnswerText {
    param($Response)
    if ($null -eq $Response) {
        return ""
    }

    $answerMarkdown = Get-ObjectPropertyValue -Object $Response -Name "answer_markdown"
    if ($null -ne $answerMarkdown) {
        return [string]$answerMarkdown
    }

    $answer = Get-ObjectPropertyValue -Object $Response -Name "answer"
    if ($null -ne $answer) {
        return [string]$answer
    }

    return ($Response | ConvertTo-Json -Depth 50)
}

function Get-MarkerHits {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string[]]$Markers
    )

    return @($Markers | Where-Object { $Text.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
}

function Read-ZipFirstText {
    param([Parameter(Mandatory = $true)][string]$Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.Entries |
            Where-Object {
                -not $_.FullName.EndsWith("/") -and
                $_.FullName -match "\.(md|markdown|txt|json|jsonl)$"
            } |
            Sort-Object FullName |
            Select-Object -First 1

        if ($null -eq $entry) {
            throw "ZIP did not contain an importable text or JSON chat-history file."
        }

        $stream = $entry.Open()
        try {
            $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
            $text = $reader.ReadToEnd()
        }
        finally {
            if ($null -ne $reader) {
                $reader.Dispose()
            }
        }

        return [pscustomobject]@{
            name = $entry.FullName
            size = [int64]$entry.Length
            modified_utc = $entry.LastWriteTime.UtcDateTime.ToString("o")
            text = $text
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-TraceSummary {
    param($Response)

    $trace = Get-ObjectPropertyValue -Object $Response -Name "trace"
    $rawDecisions = Get-ObjectPropertyValue -Object $trace -Name "memory_framework_decisions"
    $rawConflicts = Get-ObjectPropertyValue -Object $trace -Name "memory_conflicts"
    $decisions = @()
    if ($null -ne $rawDecisions) {
        $decisions = @($rawDecisions)
    }

    $conflicts = @()
    if ($null -ne $rawConflicts) {
        $conflicts = @($rawConflicts)
    }
    return [pscustomobject]@{
        trace_id = Get-ObjectPropertyValue -Object $trace -Name "trace_id"
        request_id = Get-ObjectPropertyValue -Object $trace -Name "request_id"
        route = Get-ObjectPropertyValue -Object $trace -Name "route"
        intent_type = Get-ObjectPropertyValue -Object $trace -Name "intent_type"
        memory_used = Get-ObjectPropertyValue -Object $trace -Name "memory_used"
        memory_framework_version = Get-ObjectPropertyValue -Object $trace -Name "memory_framework_version"
        memory_active_context_count = Get-ObjectPropertyValue -Object $trace -Name "memory_active_context_count"
        memory_excluded_context_count = Get-ObjectPropertyValue -Object $trace -Name "memory_excluded_context_count"
        memory_decision_count = $decisions.Count
        memory_conflict_count = $conflicts.Count
        comprehension_expression_version = Get-ObjectPropertyValue -Object $trace -Name "comprehension_expression_version"
        presentation_framework_version = Get-ObjectPropertyValue -Object $trace -Name "presentation_framework_version"
        presentation_template_family = Get-ObjectPropertyValue -Object $trace -Name "presentation_template_family"
        presentation_source_display_mode = Get-ObjectPropertyValue -Object $trace -Name "presentation_source_display_mode"
    }
}

function New-AskRecord {
    param(
        [Parameter(Mandatory = $true)][System.IO.DirectoryInfo]$Directory
    )

    $meta = Read-JsonIfPresent -Path (Join-Path $Directory.FullName "meta.json")
    $response = Read-JsonIfPresent -Path (Join-Path $Directory.FullName "response.json")
    $answer = Get-AnswerText $response
    return [pscustomobject]@{
        label = $meta.label
        user_role = $meta.user_role
        question = $meta.question
        http_status = [int]$meta.http_status
        transaction_dir = $Directory.Name
        answer = $answer
        active_hits = @(Get-MarkerHits -Text $answer -Markers $ActiveMarkers)
        excluded_hits = @(Get-MarkerHits -Text $answer -Markers $ExcludedMarkers)
        historical_hits = @(Get-MarkerHits -Text $answer -Markers $HistoricalMarkers)
        conflict_hits = @(Get-MarkerHits -Text $answer -Markers $ConflictMarkers)
        generic_filler_hits = @(Get-MarkerHits -Text $answer -Markers $GenericFillerMarkers)
        trace = Get-TraceSummary $response
    }
}

function Test-NoNonZeroCountLine {
    param([Parameter(Mandatory = $true)][string]$Path)
    $lines = Get-Content -LiteralPath $Path
    foreach ($line in $lines) {
        if ($line -match "\|(\d+)$" -and [int]$Matches[1] -ne 0) {
            return $false
        }
    }

    return $true
}

function Write-Report {
    param([Parameter(Mandatory = $true)]$Analysis)

    $status = if ($Analysis.passed) { "PASS" } else { "FAIL" }
    $tick = [char]96
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Framework Response Memory Smoke Test - $status")
    $lines.Add("")
    $lines.Add("- Run ID: $tick$RunName$tick")
    $lines.Add("- User: ${tick}superadmin / $($Analysis.auth_context.superadmin.user_id)$tick")
    $lines.Add("- Payload: ${tick}testing/chat-history-upload/framework-response-memory/CWI_Framework_Response_Memory_Test.zip$tick")
    $lines.Add("- Upload endpoint: ${tick}/api/ingest/document$tick")
    $lines.Add("- Ask endpoint: ${tick}/api/ask$tick")
    $lines.Add("")
    $lines.Add("## Checks")
    foreach ($property in $Analysis.checks.GetEnumerator()) {
        $lines.Add("- $($property.Name): ${tick}$($property.Value)$tick")
    }
    $lines.Add("")
    $lines.Add("## Ask Responses")
    foreach ($record in $Analysis.ask_records) {
        $lines.Add("### $($record.label)")
        $lines.Add("- Role: ${tick}$($record.user_role)$tick")
        $lines.Add("- HTTP: ${tick}$($record.http_status)$tick")
        $lines.Add("- Route: ${tick}$($record.trace.route)$tick")
        $lines.Add("- Memory used: ${tick}$($record.trace.memory_used)$tick")
        $lines.Add("- Memory framework: ${tick}$($record.trace.memory_framework_version)$tick")
        $lines.Add("- Decisions / conflicts: ${tick}$($record.trace.memory_decision_count) / $($record.trace.memory_conflict_count)$tick")
        $lines.Add("- Presentation: ${tick}$($record.trace.presentation_template_family) / $($record.trace.presentation_source_display_mode)$tick")
        $lines.Add("- Active hits: ${tick}$((@($record.active_hits) -join ', '))$tick")
        $lines.Add("- Excluded/historical hits: ${tick}$((@($record.excluded_hits + $record.historical_hits) -join ', '))$tick")
        $lines.Add("- Conflict hits: ${tick}$((@($record.conflict_hits) -join ', '))$tick")
        $lines.Add("")
        $lines.Add('```text')
        $preview = if ($record.answer.Length -gt 1400) { $record.answer.Substring(0, 1400) + "..." } else { $record.answer }
        $lines.Add($preview)
        $lines.Add('```')
        $lines.Add("")
    }

    ($lines -join "`n") | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}

function Build-Analysis {
    param(
        [Parameter(Mandatory = $true)]$AuthContext,
        [Parameter(Mandatory = $true)]$SourceSummary
    )

    $askRecords = @()
    foreach ($directory in Get-ChildItem -LiteralPath $AskOut -Directory | Sort-Object Name) {
        $askRecords += New-AskRecord -Directory $directory
    }

    $uploadMeta = Read-JsonIfPresent -Path (Join-Path $UploadOut "part-00001\meta.json")
    $preRecords = @($askRecords | Where-Object { $_.label -like "superadmin-pre-*" })
    $postRecords = @($askRecords | Where-Object { $_.user_role -eq "superadmin" -and $_.label -notlike "superadmin-pre-*" })
    $controlRecords = @($askRecords | Where-Object { $_.user_role -eq "control" })
    $profile = $askRecords | Where-Object label -eq "superadmin-what-do-you-know" | Select-Object -First 1
    $describe = $askRecords | Where-Object label -eq "superadmin-describe-me-as-person" | Select-Object -First 1
    $projects = $askRecords | Where-Object label -eq "superadmin-projects" | Select-Object -First 1
    $openLoops = $askRecords | Where-Object label -eq "superadmin-open-loops" | Select-Object -First 1
    $homeRecord = $askRecords | Where-Object label -eq "superadmin-home-life" | Select-Object -First 1
    $people = $askRecords | Where-Object label -eq "superadmin-key-people" | Select-Object -First 1
    $sources = $askRecords | Where-Object label -eq "superadmin-sources" | Select-Object -First 1

    $checks = [ordered]@{
        reset_after_counts_zero = Test-NoNonZeroCountLine -Path (Join-Path $RunRoot "after_counts.txt")
        pre_upload_empty_state_no_markers = @($preRecords | Where-Object { @($_.active_hits + $_.excluded_hits + $_.historical_hits + $_.conflict_hits).Count -gt 0 }).Count -eq 0
        upload_accepted = ([int]$uploadMeta.http_status -ge 200 -and [int]$uploadMeta.http_status -lt 300)
        post_http_ok = @($postRecords | Where-Object { $_.http_status -lt 200 -or $_.http_status -ge 300 }).Count -eq 0
        profile_uses_active_memory = ($profile.trace.memory_used -eq $true -and @($profile.active_hits).Count -ge 4)
        describe_is_grounded_not_generic = ($describe.trace.memory_used -eq $true -and @($describe.active_hits).Count -ge 3 -and @($describe.generic_filler_hits).Count -eq 0)
        projects_current_only = (@($projects.active_hits | Where-Object { $_ -eq "Project Cedar Lens" }).Count -gt 0 -and @($projects.excluded_hits + $projects.historical_hits).Count -eq 0)
        open_loop_current = (@($openLoops.active_hits | Where-Object { $_ -eq "OPEN-CEDAR-441" }).Count -gt 0)
        home_scope_present = (@($homeRecord.active_hits | Where-Object { $_ -eq "HOME-CEDAR-118" }).Count -gt 0)
        conflict_surfaced_not_active_truth = ((@($people.conflict_hits).Count -ge 2 -or [int]$people.trace.memory_conflict_count -gt 0) -and $people.answer.IndexOf("conflict", [StringComparison]::OrdinalIgnoreCase) -ge 0)
        sources_visible = ($sources.answer.IndexOf("Sources", [StringComparison]::OrdinalIgnoreCase) -ge 0 -and [string]$sources.trace.route -eq "activated_memory_retrieval")
        memory_framework_trace_present = @($postRecords | Where-Object { $_.trace.memory_framework_version -ne "memory-framework-v1" }).Count -eq 0
        memory_decisions_and_exclusions_present = @($postRecords | Where-Object { [int]$_.trace.memory_decision_count -le 0 -or [int]$_.trace.memory_excluded_context_count -le 0 }).Count -eq 0
        comprehension_expression_trace_present = @($postRecords | Where-Object { $_.trace.comprehension_expression_version -ne "comprehension-expression-framework-v1" }).Count -eq 0
        presentation_trace_present = @($postRecords | Where-Object { $_.trace.presentation_framework_version -ne "presentation-framework-v1" }).Count -eq 0
        source_grouping_trace_present = @($postRecords | Where-Object { $_.trace.presentation_source_display_mode -ne "source_pills_grouped" }).Count -eq 0
        control_user_isolated = @($controlRecords | Where-Object { @($_.active_hits + $_.excluded_hits + $_.historical_hits + $_.conflict_hits).Count -gt 0 }).Count -eq 0
    }

    return [pscustomobject]@{
        run_id = $RunName
        feature = $FeatureName
        public_base_url = $PublicBaseUrl
        payload = $SourceSummary
        auth_context = $AuthContext
        checks = $checks
        ask_records = $askRecords
        passed = @($checks.GetEnumerator() | Where-Object { $_.Value -ne $true }).Count -eq 0
    }
}

New-Directory $RunRoot
New-Directory $UploadOut
New-Directory $AskOut

$authAndResetScript = @'
#!/usr/bin/env bash
set -Eeuo pipefail
REMOTE_RUN_DIR="${1:?remote run dir required}"
IDENTITY_BASE="${2:?identity base url required}"
mkdir -p "$REMOTE_RUN_DIR/upload" "$REMOTE_RUN_DIR/ask"
chmod 700 "$REMOTE_RUN_DIR"
IDENTITY_CONTAINER="${CWI_SMOKE_IDENTITY_CONTAINER:-internal-01-identity-server}"
DB_CONTAINER="${CWI_SMOKE_ORCHESTRATOR_DB_CONTAINER:-internal-02-orchestrator-postgres}"

read_env() {
  docker exec "$IDENTITY_CONTAINER" printenv "$1" 2>/dev/null | tr -d '\r'
}

ADMIN_TENANT="$(read_env IDENTITY_SUPERADMIN_TENANT_ID)"
ADMIN_USERNAME="$(read_env IDENTITY_SUPERADMIN_USERNAME)"
ADMIN_PASSWORD="$(read_env IDENTITY_SUPERADMIN_PASSWORD)"
if [[ -z "$ADMIN_TENANT" || -z "$ADMIN_USERNAME" || -z "$ADMIN_PASSWORD" ]]; then
  echo "Required identity superadmin environment is unavailable in $IDENTITY_CONTAINER." >&2
  exit 2
fi

login() {
  local tenant="$1"
  local username="$2"
  local password="$3"
  jq -nc --arg tenant_id "$tenant" --arg username "$username" --arg password "$password" \
    '{tenant_id:$tenant_id,username:$username,password:$password}' |
    curl -fsS "$IDENTITY_BASE/auth/login" \
      -H 'Content-Type: application/json' \
      --data-binary @- |
    jq -er '.access_token'
}

ADMIN_TOKEN="$(login "$ADMIN_TENANT" "$ADMIN_USERNAME" "$ADMIN_PASSWORD")"
ADMIN_USER_ID="$(curl -fsS "$IDENTITY_BASE/admin/users?tenant_id=$(printf '%s' "$ADMIN_TENANT" | jq -sRr @uri)" \
  -H "Authorization: Bearer $ADMIN_TOKEN" |
  jq -er --arg username "$ADMIN_USERNAME" '.[] | select((.username // "") == $username or (.email // "") == $username) | .user_id' |
  head -n 1)"
if [[ -z "$ADMIN_USER_ID" ]]; then
  echo "Unable to resolve superadmin user id." >&2
  exit 3
fi

run_sql() {
  docker exec -i "$DB_CONTAINER" sh -lc 'PGPASSWORD="$POSTGRES_PASSWORD" psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -v ON_ERROR_STOP=1' "$@"
}

counts_sql() {
  cat <<SQL
select 'approval_tickets_pending|' || count(*) from approval_tickets where "TenantId"='$ADMIN_TENANT' and "UserId"='$ADMIN_USER_ID' and lower("Status") in ('pending','open','requires_approval','waiting','queued')
union all select 'ask_history|' || count(*) from ask_history where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID'
union all select 'conversation_summaries|' || count(*) from conversation_summaries where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID'
union all select 'governed_memory|' || count(*) from governed_memory where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID'
union all select 'memory_fact_index|' || count(*) from memory_fact_index where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID'
union all select 'source_chunks|' || count(*) from source_chunks where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID'
union all select 'source_memory|' || count(*) from source_memory where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID'
order by 1;
SQL
}

counts_sql | run_sql -At > "$REMOTE_RUN_DIR/before_counts.txt"
cat <<SQL | run_sql > "$REMOTE_RUN_DIR/reset_superadmin_context.out"
begin;
delete from source_chunks where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID';
delete from source_memory where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID';
delete from governed_memory where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID';
delete from conversation_summaries where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID';
delete from ask_history where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID';
delete from memory_fact_index where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID';
delete from normalized_connector_records where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID';
delete from connector_ingestion_receipts where tenant_id='$ADMIN_TENANT' and user_id='$ADMIN_USER_ID';
delete from approval_tickets where "TenantId"='$ADMIN_TENANT' and "UserId"='$ADMIN_USER_ID' and lower("Status") in ('pending','open','requires_approval','waiting','queued');
commit;
SQL
counts_sql | run_sql -At > "$REMOTE_RUN_DIR/after_counts.txt"

create_control_user() {
  local safe_run="framework$(date +%s)"
  local username="memory-framework-control-${safe_run}"
  local email="${username}@smoke.invalid"
  local password="SmokeT3st!${safe_run}"
  jq -nc --arg tenant_id "$ADMIN_TENANT" --arg username "$username" --arg email "$email" --arg display_name "Memory Framework Control" \
    '{tenant_id:$tenant_id,username:$username,email:$email,display_name:$display_name}' |
    curl -fsS "$IDENTITY_BASE/admin/users/invite" \
      -H "Authorization: Bearer $ADMIN_TOKEN" \
      -H 'Content-Type: application/json' \
      --data-binary @- >/dev/null
  local user_id
  user_id="$(curl -fsS "$IDENTITY_BASE/admin/users?tenant_id=$(printf '%s' "$ADMIN_TENANT" | jq -sRr @uri)" \
    -H "Authorization: Bearer $ADMIN_TOKEN" |
    jq -er --arg email "$email" '.[] | select(.email == $email) | .user_id' |
    head -n 1)"
  jq -nc --arg tenant_id "$ADMIN_TENANT" '{tenant_id:$tenant_id}' |
    curl -fsS "$IDENTITY_BASE/admin/users/$user_id/approve" \
      -H "Authorization: Bearer $ADMIN_TOKEN" \
      -H 'Content-Type: application/json' \
      --data-binary @- >/dev/null
  jq -nc --arg tenant_id "$ADMIN_TENANT" --arg password "$password" '{tenant_id:$tenant_id,password:$password}' |
    curl -fsS "$IDENTITY_BASE/admin/users/$user_id/password" \
      -H "Authorization: Bearer $ADMIN_TOKEN" \
      -H 'Content-Type: application/json' \
      --data-binary @- >/dev/null
  local token
  token="$(login "$ADMIN_TENANT" "$username" "$password")"
  printf '%s\n' "$token" > "$REMOTE_RUN_DIR/control.token"
  jq -nc --arg username "$username" --arg email "$email" --arg user_id "$user_id" \
    '{username:$username,email:$email,user_id:$user_id}' > "$REMOTE_RUN_DIR/control-user.json"
}

create_control_user
jq -n \
  --arg tenant_id "$ADMIN_TENANT" \
  --arg username "$ADMIN_USERNAME" \
  --arg user_id "$ADMIN_USER_ID" \
  --slurpfile control "$REMOTE_RUN_DIR/control-user.json" \
  '{tenant_id:$tenant_id,superadmin:{username:$username,user_id:$user_id},control_user:$control[0]}' \
  > "$REMOTE_RUN_DIR/auth-context.json"

{
  printf 'TENANT_ID=%q\n' "$ADMIN_TENANT"
  printf 'SUPERADMIN_USER_ID=%q\n' "$ADMIN_USER_ID"
  printf 'SUPERADMIN_TOKEN=%q\n' "$ADMIN_TOKEN"
  printf 'CONTROL_USER_ID=%q\n' "$(jq -r '.user_id' "$REMOTE_RUN_DIR/control-user.json")"
  printf 'CONTROL_TOKEN=%q\n' "$(cat "$REMOTE_RUN_DIR/control.token")"
  printf 'ADMIN_TOKEN=%q\n' "$ADMIN_TOKEN"
} > "$REMOTE_RUN_DIR/tokens.env"
chmod 600 "$REMOTE_RUN_DIR/tokens.env"
rm -f "$REMOTE_RUN_DIR/control.token"
'@

$sequenceScript = @'
#!/usr/bin/env bash
set -Eeuo pipefail
REMOTE_RUN_DIR="${1:?remote run dir required}"
PUBLIC_BASE_URL="${2:?public base url required}"
BROWSER_UA="${3:?browser user agent required}"
IDENTITY_BASE="${4:?identity base url required}"

source "$REMOTE_RUN_DIR/tokens.env"

cleanup() {
  if [[ -n "${CONTROL_USER_ID:-}" ]]; then
    jq -nc --arg tenant_id "$TENANT_ID" --arg reason "framework memory smoke cleanup" \
      '{tenant_id:$tenant_id,reason:$reason}' |
      curl -fsS "$IDENTITY_BASE/admin/users/$CONTROL_USER_ID/remove" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        -H 'Content-Type: application/json' \
        --data-binary @- >/dev/null || true
  fi
}
trap cleanup EXIT

call_ask() {
  local role="$1"
  local token="$2"
  local user_id="$3"
  local label="$4"
  local question="$5"
  local transaction_dir="$REMOTE_RUN_DIR/ask/$label"
  mkdir -p "$transaction_dir"

  jq -n \
    --arg tenant_id "$TENANT_ID" \
    --arg user_id "$user_id" \
    --arg question "$question" \
    '{
      tenant_id:$tenant_id,
      user_id:$user_id,
      question:$question,
      pack:{name:"personal"},
      context:{mode:"observe",top_k:8,timezone:"America/Chicago"}
    }' > "$transaction_dir/payload.json"

  local status
  status="$(curl -sS -o "$transaction_dir/response.json" -w '%{http_code}' "$PUBLIC_BASE_URL/api/ask" \
    -H "Authorization: Bearer $token" \
    -H 'Accept: application/json' \
    -H 'Content-Type: application/json' \
    -H "User-Agent: $BROWSER_UA" \
    -H 'Origin: https://cwigpt.com' \
    -H 'Referer: https://cwigpt.com/' \
    --data-binary "@$transaction_dir/payload.json")"
  jq -nc \
    --arg lbl "$label" \
    --arg role "$role" \
    --arg question "$question" \
    --arg status "$status" \
    '{"label":$lbl,"user_role":$role,"question":$question,"payload_file":"payload.json","response_file":"response.json","http_status":($status|tonumber)}' \
    > "$transaction_dir/meta.json"
}

call_ask superadmin "$SUPERADMIN_TOKEN" "$SUPERADMIN_USER_ID" superadmin-pre-what-do-you-know "what do you know about me?"

for payload in "$REMOTE_RUN_DIR"/upload/part-*/payload.json; do
  transaction_dir="$(dirname "$payload")"
  name="$(basename "$transaction_dir")"
  status="$(curl -sS -o "$transaction_dir/response.json" -w '%{http_code}' "$PUBLIC_BASE_URL/api/ingest/document" \
    -H "Authorization: Bearer $SUPERADMIN_TOKEN" \
    -H 'Accept: application/json' \
    -H 'Content-Type: application/json' \
    -H "User-Agent: $BROWSER_UA" \
    -H 'Origin: https://cwigpt.com' \
    -H 'Referer: https://cwigpt.com/' \
    --data-binary "@$payload")"
  jq -nc --arg transaction "$name" --arg status "$status" \
    '{transaction:$transaction,payload_file:"payload.json",response_file:"response.json",http_status:($status|tonumber)}' \
    > "$transaction_dir/meta.json"
done

sleep 3

call_ask superadmin "$SUPERADMIN_TOKEN" "$SUPERADMIN_USER_ID" superadmin-what-do-you-know "what do you know about me?"
call_ask superadmin "$SUPERADMIN_TOKEN" "$SUPERADMIN_USER_ID" superadmin-describe-me-as-person "Can you describe me as a person?"
call_ask superadmin "$SUPERADMIN_TOKEN" "$SUPERADMIN_USER_ID" superadmin-projects "what projects am I working on?"
call_ask superadmin "$SUPERADMIN_TOKEN" "$SUPERADMIN_USER_ID" superadmin-open-loops "what open loops do I have?"
call_ask superadmin "$SUPERADMIN_TOKEN" "$SUPERADMIN_USER_ID" superadmin-home-life "what do you know about my home life?"
call_ask superadmin "$SUPERADMIN_TOKEN" "$SUPERADMIN_USER_ID" superadmin-key-people "who are the key people in my network?"
call_ask superadmin "$SUPERADMIN_TOKEN" "$SUPERADMIN_USER_ID" superadmin-sources "show sources for what you know about me"
call_ask control "$CONTROL_TOKEN" "$CONTROL_USER_ID" control-what-do-you-know "what do you know about me?"
call_ask control "$CONTROL_TOKEN" "$CONTROL_USER_ID" control-projects "what projects am I working on?"
'@

$authScriptPath = Write-TempScript -Name "cwi-framework-auth-$RunName.sh" -Content $authAndResetScript
$sequenceScriptPath = Write-TempScript -Name "cwi-framework-sequence-$RunName.sh" -Content $sequenceScript

try {
    Invoke-Ssh "mkdir -p '$RemoteRunDir/upload' '$RemoteRunDir/ask'"
    Invoke-Scp -Arguments @($authScriptPath, "${SshTarget}:$RemoteRunDir/auth-reset.sh")
    Invoke-Ssh "chmod 700 '$RemoteRunDir/auth-reset.sh' && '$RemoteRunDir/auth-reset.sh' '$RemoteRunDir' '$IdentityBaseUrl'"
    Invoke-Scp -Arguments @("${SshTarget}:$RemoteRunDir/auth-context.json", (Join-Path $RunRoot "auth-context.json"))
    Invoke-Scp -Arguments @("${SshTarget}:$RemoteRunDir/before_counts.txt", (Join-Path $RunRoot "before_counts.txt"))
    Invoke-Scp -Arguments @("${SshTarget}:$RemoteRunDir/after_counts.txt", (Join-Path $RunRoot "after_counts.txt"))
    Invoke-Scp -Arguments @("${SshTarget}:$RemoteRunDir/reset_superadmin_context.out", (Join-Path $RunRoot "reset_superadmin_context.out"))

    $authContext = Read-JsonIfPresent -Path (Join-Path $RunRoot "auth-context.json")
    if ($null -eq $authContext) {
        throw "Auth context was not created."
    }

    $source = Read-ZipFirstText -Path $InputPath
    $batchId = "framework-$RunId"
    $docId = "web-upload-$($batchId.ToLowerInvariant())-framework-response-memory-part-00001"
    $payload = [ordered]@{
        version = "v1"
        tenant_id = $authContext.tenant_id
        user_id = $authContext.superadmin.user_id
        idempotency_key = "${batchId}:$docId"
        tags = @("gpt-web-ui", "chat-history-import", "chat_history", "framework-response-memory")
        document = [ordered]@{
            doc_id = $docId
            title = "$($source.name) (Framework Response Memory Test)"
            mime_type = "text/markdown"
            content = $source.text
            content_encoding = "plain"
            source = "cwi-gpt-web-ui"
            created_utc = $source.modified_utc
            metadata = [ordered]@{
                original_file_name = $source.name
                original_size_bytes = $source.size
                uploaded_at_utc = (Get-Date).ToUniversalTime().ToString("o")
                uploaded_via = "cwi-gpt-web-ui"
                upload_batch_id = $batchId
                uploaded_by = $authContext.superadmin.user_id
                import_kind = "chat_history"
                normalized_for_ingest = $false
                normalization_mode = "raw_chunked"
                chunk_index = 1
                source_file_index = 1
                chunk_character_count = $source.text.Length
                total_chunks = 1
                original_modified_utc = $source.modified_utc
            }
        }
    }
    $partDir = Join-Path $UploadOut "part-00001"
    New-Directory $partDir
    Write-JsonFile -Path (Join-Path $partDir "payload.json") -Value $payload
    $sourceSummary = [ordered]@{
        source_name = $source.name
        source_size_bytes = $source.size
        total_chunks = 1
        normalization_mode = "raw_chunked"
        sha256 = (Get-FileHash -LiteralPath $InputPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    Write-JsonFile -Path (Join-Path $RunRoot "source-summary.json") -Value $sourceSummary

    Invoke-Scp -Arguments @("-r", $partDir, "${SshTarget}:$RemoteRunDir/upload/")
    Invoke-Scp -Arguments @($sequenceScriptPath, "${SshTarget}:$RemoteRunDir/sequence.sh")
    Invoke-Ssh "chmod 700 '$RemoteRunDir/sequence.sh' && '$RemoteRunDir/sequence.sh' '$RemoteRunDir' '$PublicBaseUrl' '$BrowserUserAgent' '$IdentityBaseUrl'"
    Invoke-Scp -Arguments @("-r", "${SshTarget}:$RemoteRunDir/upload/.", $UploadOut)
    Invoke-Scp -Arguments @("-r", "${SshTarget}:$RemoteRunDir/ask/.", $AskOut)

    $analysis = Build-Analysis -AuthContext $authContext -SourceSummary $sourceSummary
    Write-JsonFile -Path $AnalysisPath -Value $analysis
    Write-Report -Analysis $analysis

    Write-Host "Run directory: $RunRoot"
    Write-Host "Report: $ReportPath"
    Write-Host "Passed: $($analysis.passed)"
    if (-not $analysis.passed) {
        exit 1
    }
}
catch {
    $line = $_.InvocationInfo.ScriptLineNumber
    $command = $_.InvocationInfo.Line
    Write-Error "Framework response memory smoke failed at line ${line}: $($_.Exception.Message)`n$command"
    throw
}
finally {
    Remove-Item -LiteralPath $authScriptPath, $sequenceScriptPath -Force -ErrorAction SilentlyContinue
    try {
        Invoke-Ssh "rm -f '$RemoteRunDir/auth-reset.sh' '$RemoteRunDir/sequence.sh' '$RemoteRunDir/tokens.env'"
    }
    catch {
        Write-Warning "Remote temp cleanup did not complete: $($_.Exception.Message)"
    }
}
