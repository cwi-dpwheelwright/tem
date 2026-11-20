[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateScript({ Test-Path -LiteralPath $_ })]
    [string]$PayloadPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$FeatureName = "active-context-document"
$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
$InputPath = (Resolve-Path -LiteralPath $PayloadPath).Path
$RunId = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$RunName = "$RunId-uploaded-doc-active-context"
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
$BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CWI-Smoke/1.0"

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
    $json = ConvertTo-SmokeJson -Value $Value
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($Path, $json, $utf8NoBom)
}

function ConvertTo-SmokeJson {
    param($Value)

    Add-Type -AssemblyName System.Web
    if ($null -eq $Value) {
        return "null"
    }

    if ($Value -is [string]) {
        return '"' + [System.Web.HttpUtility]::JavaScriptStringEncode($Value) + '"'
    }

    if ($Value -is [bool]) {
        return $(if ($Value) { "true" } else { "false" })
    }

    if ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal] -or $Value -is [float]) {
        return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
    }

    if ($Value -is [System.Collections.IDictionary]) {
        $items = New-Object System.Collections.Generic.List[string]
        foreach ($key in $Value.Keys) {
            $items.Add((ConvertTo-SmokeJson -Value ([string]$key)) + ":" + (ConvertTo-SmokeJson -Value $Value[$key]))
        }

        return "{" + ($items -join ",") + "}"
    }

    if ($Value -is [System.Collections.IEnumerable] -and -not ($Value -is [string])) {
        $items = New-Object System.Collections.Generic.List[string]
        foreach ($item in $Value) {
            $items.Add((ConvertTo-SmokeJson -Value $item))
        }

        return "[" + ($items -join ",") + "]"
    }

    $properties = $Value.PSObject.Properties |
        Where-Object { $_.MemberType -in @("NoteProperty", "Property") }
    if (@($properties).Count -gt 0) {
        $items = New-Object System.Collections.Generic.List[string]
        foreach ($property in $properties) {
            $items.Add((ConvertTo-SmokeJson -Value $property.Name) + ":" + (ConvertTo-SmokeJson -Value $property.Value))
        }

        return "{" + ($items -join ",") + "}"
    }

    return ConvertTo-SmokeJson -Value ([string]$Value)
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

function Get-PropertyValue {
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
    $answerMarkdown = Get-PropertyValue -Object $Response -Name "answer_markdown"
    if ($null -ne $answerMarkdown) {
        return [string]$answerMarkdown
    }

    $answer = Get-PropertyValue -Object $Response -Name "answer"
    if ($null -ne $answer) {
        return [string]$answer
    }

    return ($Response | ConvertTo-Json -Depth 50)
}

function Get-TraceSummary {
    param($Response)
    $trace = Get-PropertyValue -Object $Response -Name "trace"
    $decisions = @()
    $rawDecisions = Get-PropertyValue -Object $trace -Name "memory_framework_decisions"
    if ($null -ne $rawDecisions) {
        $decisions = @($rawDecisions)
    }

    return [pscustomobject]@{
        trace_id = Get-PropertyValue -Object $trace -Name "trace_id"
        request_id = Get-PropertyValue -Object $trace -Name "request_id"
        route = Get-PropertyValue -Object $trace -Name "route"
        intent_type = Get-PropertyValue -Object $trace -Name "intent_type"
        query_classification = Get-PropertyValue -Object $trace -Name "query_classification"
        memory_used = Get-PropertyValue -Object $trace -Name "memory_used"
        memory_active_context_count = Get-PropertyValue -Object $trace -Name "memory_active_context_count"
        memory_excluded_context_count = Get-PropertyValue -Object $trace -Name "memory_excluded_context_count"
        source_result_count = @((Get-PropertyValue -Object $trace -Name "source_results")).Count
        active_context_exclusion_count = @($decisions | Where-Object { $_.reason -eq "uploaded_document_not_confirmed_active_work" }).Count
    }
}

function New-AskRecord {
    param([Parameter(Mandatory = $true)][System.IO.DirectoryInfo]$Directory)
    $meta = Read-JsonIfPresent -Path (Join-Path $Directory.FullName "meta.json")
    $response = Read-JsonIfPresent -Path (Join-Path $Directory.FullName "response.json")
    return [pscustomobject]@{
        label = $meta.label
        question = $meta.question
        http_status = [int]$meta.http_status
        answer = Get-AnswerText $response
        trace = Get-TraceSummary $response
    }
}

function Test-ContainsAny {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string[]]$Markers
    )
    foreach ($marker in $Markers) {
        if ($Text.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }
    return $false
}

function Write-Report {
    param([Parameter(Mandatory = $true)]$Analysis)

    $status = if ($Analysis.overall_verdict -eq "PASS") { "PASS" } else { "NEEDS WORK" }
    $tick = [char]96
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Active Context Document Smoke - $status")
    $lines.Add("")
    $lines.Add("- Run ID: ${tick}$RunName$tick")
    $lines.Add("- Smoke user: ${tick}$($Analysis.auth_context.smoke_user.username) / $($Analysis.auth_context.smoke_user.user_id)$tick")
    $lines.Add("- Payload: ${tick}$InputPath$tick")
    $lines.Add("- Upload endpoint: ${tick}/api/ingest/document$tick")
    $lines.Add("- Ask endpoint: ${tick}/api/ask$tick")
    $lines.Add("")
    $lines.Add("## Evaluation")
    foreach ($item in $Analysis.evaluation) {
        $lines.Add("- $($item.name): ${tick}$($item.verdict)$tick - $($item.reason)")
    }
    $lines.Add("")
    $lines.Add("## Responses")
    foreach ($record in $Analysis.ask_records) {
        $lines.Add("### $($record.label)")
        $lines.Add("- HTTP: ${tick}$($record.http_status)$tick")
        $lines.Add("- Route: ${tick}$($record.trace.route)$tick")
        $lines.Add("- Intent: ${tick}$($record.trace.intent_type)$tick")
        $lines.Add("- Memory used: ${tick}$($record.trace.memory_used)$tick")
        $lines.Add("- Active-context exclusion count: ${tick}$($record.trace.active_context_exclusion_count)$tick")
        $lines.Add("")
        $lines.Add("Prompt:")
        $lines.Add('```text')
        $lines.Add($record.question)
        $lines.Add('```')
        $lines.Add("")
        $lines.Add("Answer:")
        $lines.Add('```text')
        $preview = if ($record.answer.Length -gt 1800) { $record.answer.Substring(0, 1800) + "..." } else { $record.answer }
        $lines.Add($preview)
        $lines.Add('```')
        $lines.Add("")
    }

    ($lines -join "`n") | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}

function Build-Analysis {
    param(
        [Parameter(Mandatory = $true)]$AuthContext,
        [Parameter(Mandatory = $true)]$UploadMeta
    )

    $records = @()
    foreach ($directory in Get-ChildItem -LiteralPath $AskOut -Directory | Sort-Object Name) {
        $records += New-AskRecord -Directory $directory
    }

    $lookup = $records | Where-Object label -eq "document-lookup" | Select-Object -First 1
    $currentWork = $records | Where-Object label -eq "current-work" | Select-Object -First 1
    $lookupMentionsPolaris = Test-ContainsAny -Text $lookup.answer -Markers @("Polaris")
    $lookupMentionsOwner = Test-ContainsAny -Text $lookup.answer -Markers @("Astrion9")
    $currentWorkMentionsPolaris = Test-ContainsAny -Text $currentWork.answer -Markers @("Polaris", "Astrion9")
    $currentWorkHasExclusion = $currentWork.trace.active_context_exclusion_count -gt 0

    $evaluation = @(
        [pscustomobject]@{
            name = "upload_accepted"
            verdict = if ([int]$UploadMeta.http_status -ge 200 -and [int]$UploadMeta.http_status -lt 300) { "PASS" } else { "FAIL" }
            reason = "Document upload should be accepted by the live ingest endpoint."
        },
        [pscustomobject]@{
            name = "document_lookup_uses_upload"
            verdict = if ($lookupMentionsPolaris -and $lookupMentionsOwner -and $lookup.trace.route -eq "uploaded_context_memory_retrieval") { "PASS" } else { "NEEDS_WORK" }
            reason = "A direct document question should explain Polaris from the uploaded context and keep the uploaded-context route."
        },
        [pscustomobject]@{
            name = "current_work_does_not_promote_uploaded_spec"
            verdict = if (-not $currentWorkMentionsPolaris -and $currentWorkHasExclusion) { "PASS" } else { "NEEDS_WORK" }
            reason = "A broad current-work question should not treat a platform charter as active work unless active-work evidence exists."
        }
    )

    $overall = if (@($evaluation | Where-Object { $_.verdict -ne "PASS" }).Count -eq 0) { "PASS" } else { "NEEDS_WORK" }
    return [pscustomobject]@{
        run_id = $RunName
        feature = $FeatureName
        auth_context = $AuthContext
        upload = $UploadMeta
        ask_records = $records
        evaluation = $evaluation
        overall_verdict = $overall
    }
}

New-Directory $RunRoot
New-Directory $UploadOut
New-Directory $AskOut

$authScript = @'
#!/usr/bin/env bash
set -Eeuo pipefail
REMOTE_RUN_DIR="${1:?remote run dir required}"
IDENTITY_BASE="${2:?identity base url required}"
mkdir -p "$REMOTE_RUN_DIR/upload" "$REMOTE_RUN_DIR/ask"
chmod 700 "$REMOTE_RUN_DIR"
IDENTITY_CONTAINER="${CWI_SMOKE_IDENTITY_CONTAINER:-internal-01-identity-server}"

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
RUN_SUFFIX="$(date +%s%N)"
SMOKE_USERNAME="active-context-smoke-$RUN_SUFFIX"
SMOKE_EMAIL="$SMOKE_USERNAME@smoke.invalid"
SMOKE_PASSWORD="SmokeT3st!$RUN_SUFFIX"

jq -nc --arg tenant_id "$ADMIN_TENANT" --arg username "$SMOKE_USERNAME" --arg email "$SMOKE_EMAIL" --arg display_name "Active Context Smoke" \
  '{tenant_id:$tenant_id,username:$username,email:$email,display_name:$display_name}' |
  curl -fsS "$IDENTITY_BASE/admin/users/invite" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H 'Content-Type: application/json' \
    --data-binary @- >/dev/null

SMOKE_USER_ID="$(curl -fsS "$IDENTITY_BASE/admin/users?tenant_id=$(printf '%s' "$ADMIN_TENANT" | jq -sRr @uri)" \
  -H "Authorization: Bearer $ADMIN_TOKEN" |
  jq -er --arg email "$SMOKE_EMAIL" '.[] | select(.email == $email) | .user_id' |
  head -n 1)"

jq -nc --arg tenant_id "$ADMIN_TENANT" '{tenant_id:$tenant_id}' |
  curl -fsS "$IDENTITY_BASE/admin/users/$SMOKE_USER_ID/approve" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H 'Content-Type: application/json' \
    --data-binary @- >/dev/null

jq -nc --arg tenant_id "$ADMIN_TENANT" --arg password "$SMOKE_PASSWORD" '{tenant_id:$tenant_id,password:$password}' |
  curl -fsS "$IDENTITY_BASE/admin/users/$SMOKE_USER_ID/password" \
    -H "Authorization: Bearer $ADMIN_TOKEN" \
    -H 'Content-Type: application/json' \
    --data-binary @- >/dev/null

SMOKE_TOKEN="$(login "$ADMIN_TENANT" "$SMOKE_USERNAME" "$SMOKE_PASSWORD")"

jq -n \
  --arg tenant_id "$ADMIN_TENANT" \
  --arg username "$SMOKE_USERNAME" \
  --arg user_id "$SMOKE_USER_ID" \
  '{tenant_id:$tenant_id,smoke_user:{username:$username,user_id:$user_id}}' \
  > "$REMOTE_RUN_DIR/auth-context.json"

{
  printf 'TENANT_ID=%q\n' "$ADMIN_TENANT"
  printf 'SMOKE_USER_ID=%q\n' "$SMOKE_USER_ID"
  printf 'SMOKE_TOKEN=%q\n' "$SMOKE_TOKEN"
  printf 'ADMIN_TOKEN=%q\n' "$ADMIN_TOKEN"
} > "$REMOTE_RUN_DIR/tokens.env"
chmod 600 "$REMOTE_RUN_DIR/tokens.env"
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
  if [[ -n "${SMOKE_USER_ID:-}" ]]; then
    jq -nc --arg tenant_id "$TENANT_ID" --arg reason "active context document smoke cleanup" \
      '{tenant_id:$tenant_id,reason:$reason}' |
      curl -fsS "$IDENTITY_BASE/admin/users/$SMOKE_USER_ID/remove" \
        -H "Authorization: Bearer $ADMIN_TOKEN" \
        -H 'Content-Type: application/json' \
        --data-binary @- >/dev/null || true
  fi
}
trap cleanup EXIT

call_ask() {
  local label="$1"
  local question="$2"
  local transaction_dir="$REMOTE_RUN_DIR/ask/$label"
  mkdir -p "$transaction_dir"

  jq -n \
    --arg tenant_id "$TENANT_ID" \
    --arg user_id "$SMOKE_USER_ID" \
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
    -H "Authorization: Bearer $SMOKE_TOKEN" \
    -H 'Accept: application/json' \
    -H 'Content-Type: application/json' \
    -H "User-Agent: $BROWSER_UA" \
    --data-binary "@$transaction_dir/payload.json")"
  jq -nc --arg lbl "$label" --arg question "$question" --arg status "$status" \
    '{label:$lbl,question:$question,payload_file:"payload.json",response_file:"response.json",http_status:($status|tonumber)}' \
    > "$transaction_dir/meta.json"
}

payload="$REMOTE_RUN_DIR/upload/part-00001/payload.json"
upload_dir="$(dirname "$payload")"
status="$(curl -sS -o "$upload_dir/response.json" -w '%{http_code}' "$PUBLIC_BASE_URL/api/ingest/document" \
  -H "Authorization: Bearer $SMOKE_TOKEN" \
  -H 'Accept: application/json' \
  -H 'Content-Type: application/json' \
  -H "User-Agent: $BROWSER_UA" \
  --data-binary "@$payload")"
jq -nc --arg status "$status" '{payload_file:"payload.json",response_file:"response.json",http_status:($status|tonumber)}' > "$upload_dir/meta.json"

sleep 3

call_ask document-lookup "Can you explain what Polaris is, the platform made by Astrion9?"
call_ask current-work "What projects am I working on?"
'@

$authScriptPath = Write-TempScript -Name "cwi-active-context-auth-$RunName.sh" -Content $authScript
$sequenceScriptPath = Write-TempScript -Name "cwi-active-context-sequence-$RunName.sh" -Content $sequenceScript

try {
    Invoke-Ssh "mkdir -p '$RemoteRunDir/upload/part-00001' '$RemoteRunDir/ask'"
    Invoke-Scp -Arguments @($authScriptPath, "${SshTarget}:$RemoteRunDir/auth.sh")
    Invoke-Ssh "chmod 700 '$RemoteRunDir/auth.sh' && '$RemoteRunDir/auth.sh' '$RemoteRunDir' '$IdentityBaseUrl'"
    Invoke-Scp -Arguments @("${SshTarget}:$RemoteRunDir/auth-context.json", (Join-Path $RunRoot "auth-context.json"))

    $authContext = Read-JsonIfPresent -Path (Join-Path $RunRoot "auth-context.json")
    $content = Get-Content -LiteralPath $InputPath -Raw
    $payload = [ordered]@{
        version = "v1"
        tenant_id = $authContext.tenant_id
        user_id = $authContext.smoke_user.user_id
        idempotency_key = "$RunName:polaris-platform-charter"
        tags = @("gpt-web-ui", "context-upload", "uploaded_context", "source-test")
        document = [ordered]@{
            doc_id = "web-upload-$($RunName.ToLowerInvariant())-polaris-charter"
            title = (Split-Path -Leaf $InputPath)
            mime_type = "text/markdown"
            content = $content
            content_encoding = "plain"
            source = "cwi-gpt-web-ui"
            created_utc = (Get-Item -LiteralPath $InputPath).LastWriteTimeUtc.ToString("o")
            metadata = [ordered]@{
                original_file_name = (Split-Path -Leaf $InputPath)
                uploaded_at_utc = (Get-Date).ToUniversalTime().ToString("o")
                uploaded_via = "cwi-gpt-web-ui"
                import_kind = "context_document"
                source_type = "uploaded_context"
                normalization_mode = "raw_context_document"
            }
        }
    }

    $partDir = Join-Path $UploadOut "part-00001"
    New-Directory $partDir
    Write-JsonFile -Path (Join-Path $partDir "payload.json") -Value $payload
    Invoke-Scp -Arguments @("-r", $partDir, "${SshTarget}:$RemoteRunDir/upload/")
    Invoke-Scp -Arguments @($sequenceScriptPath, "${SshTarget}:$RemoteRunDir/sequence.sh")
    Invoke-Ssh "chmod 700 '$RemoteRunDir/sequence.sh' && '$RemoteRunDir/sequence.sh' '$RemoteRunDir' '$PublicBaseUrl' '$BrowserUserAgent' '$IdentityBaseUrl'"
    Invoke-Scp -Arguments @("-r", "${SshTarget}:$RemoteRunDir/upload/.", $UploadOut)
    Invoke-Scp -Arguments @("-r", "${SshTarget}:$RemoteRunDir/ask/.", $AskOut)

    $uploadMeta = Read-JsonIfPresent -Path (Join-Path $UploadOut "part-00001\meta.json")
    $analysis = Build-Analysis -AuthContext $authContext -UploadMeta $uploadMeta
    Write-JsonFile -Path $AnalysisPath -Value $analysis
    Write-Report -Analysis $analysis

    Write-Host "Run directory: $RunRoot"
    Write-Host "Report: $ReportPath"
    Write-Host "Verdict: $($analysis.overall_verdict)"
}
finally {
    Remove-Item -LiteralPath $authScriptPath, $sequenceScriptPath -Force -ErrorAction SilentlyContinue
    try {
        Invoke-Ssh "rm -f '$RemoteRunDir/auth.sh' '$RemoteRunDir/sequence.sh' '$RemoteRunDir/tokens.env'"
    }
    catch {
        Write-Warning "Remote temp cleanup did not complete: $($_.Exception.Message)"
    }
}
