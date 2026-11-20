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
$RunRoot = Join-Path $RepoRoot "testing\runs\$FeatureName\$RunId"
$UploadOut = Join-Path $RunRoot "upload"
$AskOut = Join-Path $RunRoot "ask"
$ReportPath = Join-Path $RunRoot "report.md"
$AnalysisPath = Join-Path $RunRoot "analysis.json"

$PublicBaseUrl = if ($env:CWI_SMOKE_PUBLIC_BASE_URL) { $env:CWI_SMOKE_PUBLIC_BASE_URL.TrimEnd("/") } else { "https://cwigpt.com" }
$SshTarget = if ($env:CWI_SMOKE_SSH_TARGET) {
    $env:CWI_SMOKE_SSH_TARGET
}
else {
    throw "CWI_SMOKE_SSH_TARGET must be set to the SSH target that can reach the deployed CWI stack."
}
$IdentityBaseUrl = if ($env:CWI_SMOKE_IDENTITY_BASE_URL) { $env:CWI_SMOKE_IDENTITY_BASE_URL.TrimEnd("/") } else { "http://127.0.0.1:8085" }
$TenantId = if ($env:CWI_SMOKE_TENANT_ID) { $env:CWI_SMOKE_TENANT_ID } else { "tenant-1" }
$RemoteRoot = if ($env:CWI_SMOKE_REMOTE_ROOT) { $env:CWI_SMOKE_REMOTE_ROOT.TrimEnd("/") } else { "/tmp/cwi-smoke" }
$RemoteRunDir = "$RemoteRoot/$FeatureName/$RunId"
$ChunkChars = 500000
$BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"

$Markers = @(
    "Avery Testwell",
    "BLUELANTERN-7429",
    "Project Quartz Lantern",
    "OPEN-LOOP-GAMMA-17",
    "OPEN-LOOP-NEBULA-88",
    "LATE-CHUNK-MARIGOLD-204",
    "DECISION-EMBER-31",
    "HOME-ORCHID-58",
    "GOAL-AURORA-19",
    "Demitrius",
    "Chad",
    "Jasmin"
)

$ImportingUserPrompts = @(
    @{ label = "importing-what-do-you-know"; question = "what do you know about me?" },
    @{ label = "importing-projects"; question = "what projects am I working on?" },
    @{ label = "importing-key-people"; question = "who are the key people in my network?" },
    @{ label = "importing-open-loops"; question = "what open loops do I have?" },
    @{ label = "importing-sources"; question = "show sources for what you know about me" }
)

$ControlUserPrompts = @(
    @{ label = "control-what-do-you-know"; question = "what do you know about me?" },
    @{ label = "control-projects"; question = "what projects am I working on?" }
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
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $json, $utf8NoBom)
}

function Convert-ToSafeSegment {
    param([Parameter(Mandatory = $true)][string]$Value)
    $safe = $Value.ToLowerInvariant() -replace "[^a-z0-9]+", "-"
    $safe = $safe.Trim("-")
    if ([string]::IsNullOrWhiteSpace($safe)) {
        return "upload"
    }
    return $safe
}

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-MimeType {
    param([Parameter(Mandatory = $true)][string]$Name)
    $extension = [System.IO.Path]::GetExtension($Name).TrimStart(".").ToLowerInvariant()
    switch ($extension) {
        "json" { return "application/json" }
        "jsonl" { return "application/json" }
        "md" { return "text/markdown" }
        "markdown" { return "text/markdown" }
        "csv" { return "text/csv" }
        "tsv" { return "text/tab-separated-values" }
        "html" { return "text/html" }
        "htm" { return "text/html" }
        "xml" { return "application/xml" }
        "yaml" { return "application/yaml" }
        "yml" { return "application/yaml" }
        default { return "text/plain" }
    }
}

function Test-ImportableFileName {
    param([Parameter(Mandatory = $true)][string]$Name)
    $extension = [System.IO.Path]::GetExtension($Name).TrimStart(".").ToLowerInvariant()
    return @("json", "jsonl", "txt", "md", "markdown", "csv", "tsv", "log", "xml", "yaml", "yml", "html", "htm") -contains $extension
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

function Read-TextFromPayload {
    param([Parameter(Mandatory = $true)][string]$Path)

    $extension = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
    if ($extension -eq ".zip") {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
        try {
            $entry = $archive.Entries |
                Where-Object {
                    -not $_.FullName.EndsWith("/") -and
                    $_.FullName -notmatch "(^|/)__MACOSX(/|$)" -and
                    $_.FullName -notmatch "(^|/)\._" -and
                    (Test-ImportableFileName $_.FullName)
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
                if ($null -ne $reader) { $reader.Dispose() }
            }

            return [pscustomobject]@{
                source_name = $entry.FullName
                source_size_bytes = [int64]$entry.Length
                source_modified_utc = $entry.LastWriteTime.UtcDateTime.ToString("o")
                text = $text
            }
        }
        finally {
            $archive.Dispose()
        }
    }

    $rawText = [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
    $item = Get-Item -LiteralPath $Path
    return [pscustomobject]@{
        source_name = $item.Name
        source_size_bytes = [int64]$item.Length
        source_modified_utc = $item.LastWriteTimeUtc.ToString("o")
        text = $rawText
    }
}

function Get-ChatMessageText {
    param($Message)

    $content = Get-ObjectPropertyValue -Object $Message -Name "content"
    if ($null -eq $Message -or $null -eq $content) {
        return ""
    }

    $parts = @()

    $contentParts = Get-ObjectPropertyValue -Object $content -Name "parts"
    $contentText = Get-ObjectPropertyValue -Object $content -Name "text"
    if ($null -ne $contentParts) {
        foreach ($part in $contentParts) {
            if ($part -is [string]) {
                $parts += $part
            }
            else {
                $partText = Get-ObjectPropertyValue -Object $part -Name "text"
                $partValue = Get-ObjectPropertyValue -Object $part -Name "value"
                if ($null -ne $partText) {
                    $parts += [string]$partText
                }
                elseif ($null -ne $partValue) {
                    $parts += [string]$partValue
                }
            }
        }
    }
    elseif ($null -ne $contentText) {
        $parts += [string]$contentText
    }
    elseif ($content -is [string]) {
        $parts += $content
    }

    return (($parts | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "`n").Trim()
}

function Convert-ChatHistoryToTranscript {
    param([Parameter(Mandatory = $true)][string]$Text)

    try {
        $parsed = $Text | ConvertFrom-Json
    }
    catch {
        return [pscustomobject]@{
            transcript = $Text
            message_count = $null
            normalization_mode = "raw_chunked"
            import_kind = "chat_history"
        }
    }

    $mapping = Get-ObjectPropertyValue -Object $parsed -Name "mapping"
    if ($null -eq $mapping) {
        return [pscustomobject]@{
            transcript = $Text
            message_count = $null
            normalization_mode = "raw_chunked"
            import_kind = "chat_history"
        }
    }

    $messages = @()
    foreach ($property in $mapping.PSObject.Properties) {
        $message = Get-ObjectPropertyValue -Object $property.Value -Name "message"
        if ($null -eq $message) {
            continue
        }

        $author = Get-ObjectPropertyValue -Object $message -Name "author"
        $roleValue = Get-ObjectPropertyValue -Object $author -Name "role"
        $role = if ($null -ne $roleValue) { [string]$roleValue } else { "unknown" }
        if ($role -in @("system", "tool")) {
            continue
        }

        $messageText = Get-ChatMessageText $message
        if ([string]::IsNullOrWhiteSpace($messageText)) {
            continue
        }

        $createTime = Get-ObjectPropertyValue -Object $message -Name "create_time"
        $created = if ($null -ne $createTime) { [double]$createTime } else { 0.0 }
        $messages += [pscustomobject]@{
            created = $created
            role = $role
            text = $messageText
        }
    }

    if ($messages.Count -eq 0) {
        return [pscustomobject]@{
            transcript = $Text
            message_count = $null
            normalization_mode = "raw_chunked"
            import_kind = "chat_history"
        }
    }

    $lines = New-Object System.Collections.Generic.List[string]
    foreach ($message in ($messages | Sort-Object created)) {
        $label = switch ($message.role.ToLowerInvariant()) {
            "assistant" { "Assistant" }
            "user" { "User" }
            default { $message.role }
        }
        $lines.Add("${label}: $($message.text)")
        $lines.Add("")
    }

    return [pscustomobject]@{
        transcript = (($lines -join "`n").Trim())
        message_count = $messages.Count
        normalization_mode = "structured_transcript"
        import_kind = "chat_history"
    }
}

function Find-SplitIndex {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][int]$MaxChars
    )
    if ($Value.Length -le $MaxChars) {
        return $Value.Length
    }

    $floor = [Math]::Floor($MaxChars * 0.6)
    foreach ($separator in @("`n`n", "`n", ". ", " ")) {
        $position = $Value.LastIndexOf($separator, [Math]::Min($MaxChars, $Value.Length - 1), [StringComparison]::Ordinal)
        if ($position -ge $floor) {
            return $position + $separator.Length
        }
    }

    return $MaxChars
}

function Split-TextIntoSegments {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][int]$MaxChars
    )

    $remaining = ($Value -replace "`0", "" -replace "`r`n", "`n").Trim()
    $segments = New-Object System.Collections.Generic.List[string]
    while (-not [string]::IsNullOrWhiteSpace($remaining)) {
        if ($remaining.Length -le $MaxChars) {
            $segments.Add($remaining)
            break
        }

        $splitIndex = Find-SplitIndex -Value $remaining -MaxChars $MaxChars
        $segment = $remaining.Substring(0, $splitIndex).Trim()
        if (-not [string]::IsNullOrWhiteSpace($segment)) {
            $segments.Add($segment)
        }
        $remaining = $remaining.Substring($splitIndex).Trim()
    }

    return $segments
}

function New-IngestPayloads {
    param(
        [Parameter(Mandatory = $true)]$Source,
        [Parameter(Mandatory = $true)]$Normalized,
        [Parameter(Mandatory = $true)][string]$TenantId,
        [Parameter(Mandatory = $true)][string]$UserId,
        [Parameter(Mandatory = $true)][string]$UploadBatchId,
        [Parameter(Mandatory = $true)][string]$OutputDirectory
    )

    $segments = Split-TextIntoSegments -Value $Normalized.transcript -MaxChars $ChunkChars
    $safeBatch = Convert-ToSafeSegment $UploadBatchId
    $safeFile = Convert-ToSafeSegment $Source.source_name
    $totalChunks = $segments.Count
    $uploadedAt = (Get-Date).ToUniversalTime().ToString("o")
    $mimeType = Get-MimeType $Source.source_name

    for ($index = 0; $index -lt $segments.Count; $index++) {
        $chunkOrdinal = $index + 1
        $docId = "web-upload-$safeBatch-file-001-$safeFile-part-$($chunkOrdinal.ToString("00000"))"
        $metadata = [ordered]@{
            original_file_name = $Source.source_name
            original_size_bytes = $Source.source_size_bytes
            uploaded_at_utc = $uploadedAt
            uploaded_via = "cwi-gpt-web-ui"
            upload_batch_id = $UploadBatchId
            uploaded_by = $UserId
            import_kind = $Normalized.import_kind
            normalized_for_ingest = ($Normalized.normalization_mode -eq "structured_transcript")
            normalization_mode = $Normalized.normalization_mode
            chunk_index = $chunkOrdinal
            source_file_index = 1
            chunk_character_count = $segments[$index].Length
            total_chunks = $totalChunks
            original_modified_utc = $Source.source_modified_utc
        }
        if ($null -ne $Normalized.message_count) {
            $metadata["message_count"] = $Normalized.message_count
        }

        $payload = [ordered]@{
            version = "v1"
            tenant_id = $TenantId
            user_id = $UserId
            idempotency_key = "${UploadBatchId}:$docId"
            tags = @("gpt-web-ui", "chat-history-import", $Normalized.import_kind, $Normalized.normalization_mode)
            document = [ordered]@{
                doc_id = $docId
                title = "$($Source.source_name) (Part $chunkOrdinal of $totalChunks)"
                mime_type = $mimeType
                content = $segments[$index]
                content_encoding = "plain"
                source = "cwi-gpt-web-ui"
                created_utc = $Source.source_modified_utc
                metadata = $metadata
            }
        }

        $transactionDirectory = Join-Path $OutputDirectory ("part-{0}" -f $chunkOrdinal.ToString("00000"))
        New-Directory $transactionDirectory
        $payloadPath = Join-Path $transactionDirectory "payload.json"
        Write-JsonFile -Path $payloadPath -Value $payload
    }

    return [pscustomobject]@{
        total_chunks = $totalChunks
        message_count = $Normalized.message_count
        normalization_mode = $Normalized.normalization_mode
        import_kind = $Normalized.import_kind
        source_name = $Source.source_name
        source_size_bytes = $Source.source_size_bytes
    }
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
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
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
    param([Parameter(Mandatory = $true)][string]$Text)
    $hits = @()
    foreach ($marker in $Markers) {
        if ($Text.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $hits += $marker
        }
    }
    return $hits
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

function Analyze-SmokeRun {
    param(
        [Parameter(Mandatory = $true)]$PayloadSummary,
        [Parameter(Mandatory = $true)]$AuthContext
    )

    $uploadRecords = @()
    foreach ($transactionDirectory in Get-ChildItem -LiteralPath $UploadOut -Directory -ErrorAction SilentlyContinue) {
        $metaPath = Join-Path $transactionDirectory.FullName "meta.json"
        $meta = Read-JsonIfPresent -Path $metaPath
        if ($null -ne $meta) {
            $uploadRecords += $meta
        }
    }

    $askRecords = @()
    foreach ($transactionDirectory in Get-ChildItem -LiteralPath $AskOut -Directory -ErrorAction SilentlyContinue) {
        $metaPath = Join-Path $transactionDirectory.FullName "meta.json"
        $meta = Read-JsonIfPresent -Path $metaPath
        if ($null -eq $meta) {
            continue
        }
        $responsePath = Join-Path $transactionDirectory.FullName "response.json"
        $response = Read-JsonIfPresent -Path $responsePath
        $text = Get-AnswerText $response
        $trace = Get-ObjectPropertyValue -Object $response -Name "trace"
        $sources = Get-ObjectPropertyValue -Object $trace -Name "sources"
        $askRecords += [pscustomobject]@{
            label = $meta.label
            user_role = $meta.user_role
            question = $meta.question
            http_status = [int]$meta.http_status
            transaction_dir = $transactionDirectory.Name
            payload_file = "payload.json"
            response_file = "response.json"
            marker_hits = @(Get-MarkerHits $text)
            answer_preview = if ($text.Length -gt 700) { $text.Substring(0, 700) } else { $text }
            trace = [pscustomobject]@{
                trace_id = Get-ObjectPropertyValue -Object $trace -Name "trace_id"
                request_id = Get-ObjectPropertyValue -Object $trace -Name "request_id"
                route = Get-ObjectPropertyValue -Object $trace -Name "route"
                intent_type = Get-ObjectPropertyValue -Object $trace -Name "intent_type"
                confidence = Get-ObjectPropertyValue -Object $trace -Name "confidence"
                memory_used = Get-ObjectPropertyValue -Object $trace -Name "memory_used"
                retrieval_status = Get-ObjectPropertyValue -Object $trace -Name "retrieval_status"
                sources_count = if ($null -ne $sources) { @($sources).Count } else { 0 }
            }
        }
    }

    $uploadOk = @($uploadRecords).Count -eq $PayloadSummary.total_chunks -and @($uploadRecords | Where-Object { [int]$_.http_status -lt 200 -or [int]$_.http_status -ge 300 }).Count -eq 0
    $importingAsk = @($askRecords | Where-Object { $_.user_role -eq "importing" -and $_.label -notlike "importing-pre*" })
    $controlAsk = @($askRecords | Where-Object { $_.user_role -eq "control" })
    $importingMarkerHits = @($importingAsk | ForEach-Object { $_.marker_hits } | Where-Object { $_ })
    $controlMarkerHits = @($controlAsk | ForEach-Object { $_.marker_hits } | Where-Object { $_ })
    $memoryUsed = @($importingAsk | Where-Object { $_.trace.memory_used -eq $true }).Count -gt 0
    $importingAnswersUsedMemory = @($importingMarkerHits).Count -gt 0
    $controlIsolationOk = @($controlMarkerHits).Count -eq 0
    $askHttpOk = @($askRecords | Where-Object { $_.http_status -lt 200 -or $_.http_status -ge 300 }).Count -eq 0
    $passed = $uploadOk -and $askHttpOk -and $memoryUsed -and $importingAnswersUsedMemory -and $controlIsolationOk

    return [pscustomobject]@{
        run_id = $RunId
        feature = $FeatureName
        started_utc = (Get-Date).ToUniversalTime().ToString("o")
        public_base_url = $PublicBaseUrl
        input_payload = [pscustomobject]@{
            path = $InputPath
            sha256 = Get-FileSha256 $InputPath
            source_name = $PayloadSummary.source_name
            source_size_bytes = $PayloadSummary.source_size_bytes
            normalization_mode = $PayloadSummary.normalization_mode
            message_count = $PayloadSummary.message_count
            total_chunks = $PayloadSummary.total_chunks
        }
        auth_context = $AuthContext
        checks = [ordered]@{
            upload_all_chunks_accepted = $uploadOk
            ask_requests_http_ok = $askHttpOk
            importing_user_memory_used_trace = $memoryUsed
            importing_user_answer_contains_imported_markers = $importingAnswersUsedMemory
            control_user_did_not_receive_imported_markers = $controlIsolationOk
        }
        marker_hits = [ordered]@{
            importing_user = @($importingMarkerHits | Select-Object -Unique)
            control_user = @($controlMarkerHits | Select-Object -Unique)
        }
        upload_records = $uploadRecords
        ask_records = $askRecords
        passed = $passed
    }
}

function Write-Report {
    param([Parameter(Mandatory = $true)]$Analysis)

    $status = if ($Analysis.passed) { "PASS" } else { "FAIL" }
    $tick = [char]96
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Chat History Upload Smoke Test - $status")
    $lines.Add("")
    $lines.Add("- Run ID: $tick$($Analysis.run_id)$tick")
    $lines.Add("- Public base URL: $tick$($Analysis.public_base_url)$tick")
    $lines.Add("- Payload SHA256: $tick$($Analysis.input_payload.sha256)$tick")
    $lines.Add("- Source file inside payload: $tick$($Analysis.input_payload.source_name)$tick")
    $lines.Add("- Normalization mode: $tick$($Analysis.input_payload.normalization_mode)$tick")
    $lines.Add("- Message count: $tick$($Analysis.input_payload.message_count)$tick")
    $lines.Add("- Upload chunks: $tick$($Analysis.input_payload.total_chunks)$tick")
    $lines.Add("- Importing user: $tick$($Analysis.auth_context.importing_user.username)$tick / $tick$($Analysis.auth_context.importing_user.user_id)$tick")
    $lines.Add("- Control user: $tick$($Analysis.auth_context.control_user.username)$tick / $tick$($Analysis.auth_context.control_user.user_id)$tick")
    $lines.Add("")
    $lines.Add("## Checks")
    foreach ($property in $Analysis.checks.GetEnumerator()) {
        $lines.Add("- $($property.Name): $tick$($property.Value)$tick")
    }
    $lines.Add("")
    $lines.Add("## Marker Evidence")
    $lines.Add("- Importing user marker hits: $tick$((@($Analysis.marker_hits.importing_user) -join ', '))$tick")
    $lines.Add("- Control user marker hits: $tick$((@($Analysis.marker_hits.control_user) -join ', '))$tick")
    $lines.Add("")
    $lines.Add("## Ask Responses")
    foreach ($record in $Analysis.ask_records) {
        $lines.Add("### $($record.label)")
        $lines.Add("- User role: $tick$($record.user_role)$tick")
        $lines.Add("- HTTP status: $tick$($record.http_status)$tick")
        $lines.Add("- Question: $($record.question)")
        $lines.Add("- Trace ID: $tick$($record.trace.trace_id)$tick")
        $lines.Add("- Route: $tick$($record.trace.route)$tick")
        $lines.Add("- Memory used: $tick$($record.trace.memory_used)$tick")
        $lines.Add("- Marker hits: $tick$((@($record.marker_hits) -join ', '))$tick")
        $lines.Add("")
        $lines.Add("Preview:")
        $lines.Add("")
        $lines.Add("$tick$tick${tick}text")
        $lines.Add($record.answer_preview)
        $lines.Add("$tick$tick$tick")
        $lines.Add("")
    }

    $lines -join "`n" | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}

New-Directory $RunRoot
New-Directory $UploadOut
New-Directory $AskOut

$authScript = @'
#!/usr/bin/env bash
set -Eeuo pipefail

REMOTE_RUN_DIR="${1:?remote run dir required}"
TENANT_ID="${2:?tenant id required}"
RUN_ID="${3:?run id required}"
IDENTITY_BASE="${4:?identity base url required}"

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

create_smoke_user() {
  local role="$1"
  local safe_run
  safe_run="$(printf '%s' "$RUN_ID" | tr '[:upper:]' '[:lower:]' | tr -c 'a-z0-9' '-')"
  local username="memory-smoke-${role}-${safe_run}"
  local email="${username}@smoke.invalid"
  local display_name="Memory Smoke ${role}"
  local password="SmokeT3st!${safe_run}${role}"

  jq -nc --arg tenant_id "$TENANT_ID" --arg username "$username" --arg email "$email" --arg display_name "$display_name" \
    '{tenant_id:$tenant_id,username:$username,email:$email,display_name:$display_name}' |
    curl -fsS "$IDENTITY_BASE/admin/users/invite" \
      -H "Authorization: Bearer $ADMIN_TOKEN" \
      -H 'Content-Type: application/json' \
      --data-binary @- >/dev/null

  local user_id
  user_id="$(curl -fsS "$IDENTITY_BASE/admin/users?tenant_id=$(printf '%s' "$TENANT_ID" | jq -sRr @uri)" \
    -H "Authorization: Bearer $ADMIN_TOKEN" |
    jq -er --arg email "$email" '.[] | select(.email == $email) | .user_id' | head -n 1)"

  jq -nc --arg tenant_id "$TENANT_ID" '{tenant_id:$tenant_id}' |
    curl -fsS "$IDENTITY_BASE/admin/users/$user_id/approve" \
      -H "Authorization: Bearer $ADMIN_TOKEN" \
      -H 'Content-Type: application/json' \
      --data-binary @- >/dev/null

  jq -nc --arg tenant_id "$TENANT_ID" --arg password "$password" '{tenant_id:$tenant_id,password:$password}' |
    curl -fsS "$IDENTITY_BASE/admin/users/$user_id/password" \
      -H "Authorization: Bearer $ADMIN_TOKEN" \
      -H 'Content-Type: application/json' \
      --data-binary @- >/dev/null

  local token
  token="$(login "$TENANT_ID" "$username" "$password")"

  jq -nc --arg role "$role" --arg username "$username" --arg email "$email" --arg user_id "$user_id" \
    '{role:$role,username:$username,email:$email,user_id:$user_id,created_for:"chat-history-upload-smoke"}'
  printf '%s\n' "$token" > "$REMOTE_RUN_DIR/${role}.token"
  chmod 600 "$REMOTE_RUN_DIR/${role}.token"
}

IMPORTING_CONTEXT="$(create_smoke_user importing)"
CONTROL_CONTEXT="$(create_smoke_user control)"

jq -n \
  --arg run_id "$RUN_ID" \
  --arg tenant_id "$TENANT_ID" \
  --argjson importing "$IMPORTING_CONTEXT" \
  --argjson control "$CONTROL_CONTEXT" \
  '{run_id:$run_id,tenant_id:$tenant_id,importing_user:$importing,control_user:$control}' \
  > "$REMOTE_RUN_DIR/auth-context.json"

{
  printf 'TENANT_ID=%q\n' "$TENANT_ID"
  printf 'IMPORTING_USER_ID=%q\n' "$(jq -r '.user_id' <<<"$IMPORTING_CONTEXT")"
  printf 'CONTROL_USER_ID=%q\n' "$(jq -r '.user_id' <<<"$CONTROL_CONTEXT")"
  printf 'IMPORTING_TOKEN=%q\n' "$(cat "$REMOTE_RUN_DIR/importing.token")"
  printf 'CONTROL_TOKEN=%q\n' "$(cat "$REMOTE_RUN_DIR/control.token")"
  printf 'ADMIN_TOKEN=%q\n' "$ADMIN_TOKEN"
} > "$REMOTE_RUN_DIR/tokens.env"
chmod 600 "$REMOTE_RUN_DIR/tokens.env"
rm -f "$REMOTE_RUN_DIR/importing.token" "$REMOTE_RUN_DIR/control.token"
'@

$sequenceScript = @'
#!/usr/bin/env bash
set -Eeuo pipefail

REMOTE_RUN_DIR="${1:?remote run dir required}"
PUBLIC_BASE_URL="${2:?public base url required}"
BROWSER_UA="${3:?browser user agent required}"
IDENTITY_BASE="${4:?identity base url required}"

source "$REMOTE_RUN_DIR/tokens.env"

mkdir -p "$REMOTE_RUN_DIR/upload" "$REMOTE_RUN_DIR/ask"

cleanup() {
  local reason='chat-history-upload smoke cleanup'
  for user_id in "${IMPORTING_USER_ID:-}" "${CONTROL_USER_ID:-}"; do
    if [[ -n "$user_id" ]]; then
      jq -nc --arg tenant_id "$TENANT_ID" --arg reason "$reason" '{tenant_id:$tenant_id,reason:$reason}' |
        curl -fsS "$IDENTITY_BASE/admin/users/$user_id/remove" \
          -H "Authorization: Bearer $ADMIN_TOKEN" \
          -H 'Content-Type: application/json' \
          -H "User-Agent: $BROWSER_UA" \
          --data-binary @- >/dev/null || true
    fi
  done
}
trap cleanup EXIT

call_ask() {
  local role="$1"
  local token="$2"
  local user_id="$3"
  local label="$4"
  local question="$5"
  local transaction_dir="$REMOTE_RUN_DIR/ask/$label"
  local request_file="$transaction_dir/payload.json"
  local response_path="$transaction_dir/response.json"
  local status
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
    }' > "$request_file"

  status="$(curl -sS -o "$response_path" -w '%{http_code}' "$PUBLIC_BASE_URL/api/ask" \
    -H "Authorization: Bearer $token" \
    -H 'Accept: application/json' \
    -H 'Content-Type: application/json' \
    -H "User-Agent: $BROWSER_UA" \
    -H 'Origin: https://cwigpt.com' \
    -H 'Referer: https://cwigpt.com/' \
    --data-binary "@$request_file")"

  jq -n \
    --arg lbl "$label" \
    --arg user_role "$role" \
    --arg question "$question" \
    --arg status "$status" \
    '{"label":$lbl,"user_role":$user_role,"question":$question,"payload_file":"payload.json","response_file":"response.json","http_status":($status|tonumber)}' \
    > "$transaction_dir/meta.json"
}

call_ask importing "$IMPORTING_TOKEN" "$IMPORTING_USER_ID" "importing-pre-what-do-you-know" "what do you know about me?"

for payload in "$REMOTE_RUN_DIR"/upload/part-*/payload.json; do
  transaction_dir="$(dirname "$payload")"
  name="$(basename "$transaction_dir")"
  response_file="$transaction_dir/response.json"
  status="$(curl -sS -o "$response_file" -w '%{http_code}' "$PUBLIC_BASE_URL/api/ingest/document" \
    -H "Authorization: Bearer $IMPORTING_TOKEN" \
    -H 'Accept: application/json' \
    -H 'Content-Type: application/json' \
    -H "User-Agent: $BROWSER_UA" \
    -H 'Origin: https://cwigpt.com' \
    -H 'Referer: https://cwigpt.com/' \
    --data-binary "@$payload")"
  jq -nc --arg transaction "$name" --arg status "$status" \
    '{"transaction":$transaction,"payload_file":"payload.json","response_file":"response.json","http_status":($status|tonumber)}' > "$transaction_dir/meta.json"
done

sleep 3

call_ask importing "$IMPORTING_TOKEN" "$IMPORTING_USER_ID" "importing-what-do-you-know" "what do you know about me?"
call_ask importing "$IMPORTING_TOKEN" "$IMPORTING_USER_ID" "importing-projects" "what projects am I working on?"
call_ask importing "$IMPORTING_TOKEN" "$IMPORTING_USER_ID" "importing-key-people" "who are the key people in my network?"
call_ask importing "$IMPORTING_TOKEN" "$IMPORTING_USER_ID" "importing-open-loops" "what open loops do I have?"
call_ask importing "$IMPORTING_TOKEN" "$IMPORTING_USER_ID" "importing-sources" "show sources for what you know about me"

call_ask control "$CONTROL_TOKEN" "$CONTROL_USER_ID" "control-what-do-you-know" "what do you know about me?"
call_ask control "$CONTROL_TOKEN" "$CONTROL_USER_ID" "control-projects" "what projects am I working on?"
'@

$authScriptPath = Write-TempScript -Name "cwi-smoke-auth-$RunId.sh" -Content $authScript
$sequenceScriptPath = Write-TempScript -Name "cwi-smoke-sequence-$RunId.sh" -Content $sequenceScript

try {
    Invoke-Ssh "mkdir -p '$RemoteRunDir/upload' '$RemoteRunDir/ask'"
    Invoke-Scp -Arguments @($authScriptPath, "${SshTarget}:$RemoteRunDir/auth.sh")
    Invoke-Ssh "chmod 700 '$RemoteRunDir/auth.sh' && '$RemoteRunDir/auth.sh' '$RemoteRunDir' '$TenantId' '$RunId' '$IdentityBaseUrl'"
    Invoke-Scp -Arguments @("${SshTarget}:$RemoteRunDir/auth-context.json", (Join-Path $RunRoot "auth-context.json"))

    $authContext = Read-JsonIfPresent -Path (Join-Path $RunRoot "auth-context.json")
    if ($null -eq $authContext) {
        throw "Auth context was not created."
    }

    $source = Read-TextFromPayload -Path $InputPath
    $normalized = Convert-ChatHistoryToTranscript -Text $source.text
    $uploadBatchId = "smoke-$RunId"
    $payloadSummary = New-IngestPayloads `
        -Source $source `
        -Normalized $normalized `
        -TenantId $authContext.tenant_id `
        -UserId $authContext.importing_user.user_id `
        -UploadBatchId $uploadBatchId `
        -OutputDirectory $UploadOut

    Write-JsonFile -Path (Join-Path $RunRoot "source-summary.json") -Value $payloadSummary

    foreach ($transactionDirectory in Get-ChildItem -LiteralPath $UploadOut -Directory) {
        Invoke-Scp -Arguments @("-r", $transactionDirectory.FullName, "${SshTarget}:$RemoteRunDir/upload/")
    }

    Invoke-Scp -Arguments @($sequenceScriptPath, "${SshTarget}:$RemoteRunDir/sequence.sh")
    Invoke-Ssh "chmod 700 '$RemoteRunDir/sequence.sh' && '$RemoteRunDir/sequence.sh' '$RemoteRunDir' '$PublicBaseUrl' '$BrowserUserAgent' '$IdentityBaseUrl'"
    Invoke-Scp -Arguments @("-r", "${SshTarget}:$RemoteRunDir/upload/.", $UploadOut)
    Invoke-Scp -Arguments @("-r", "${SshTarget}:$RemoteRunDir/ask/.", $AskOut)

    $analysis = Analyze-SmokeRun -PayloadSummary $payloadSummary -AuthContext $authContext
    Write-JsonFile -Path $AnalysisPath -Value $analysis
    Write-Report -Analysis $analysis

    if ($analysis.passed) {
        Write-Host "PASS: chat-history upload smoke test completed."
    }
    else {
        Write-Host "FAIL: chat-history upload smoke test completed with failing checks."
    }
    Write-Host "Run directory: $RunRoot"
    Write-Host "Report: $ReportPath"

    if (-not $analysis.passed) {
        exit 1
    }
}
catch {
    $line = $_.InvocationInfo.ScriptLineNumber
    $command = $_.InvocationInfo.Line
    Write-Error "Smoke test failed at line ${line}: $($_.Exception.Message)`n$command"
    throw
}
finally {
    Remove-Item -LiteralPath $authScriptPath, $sequenceScriptPath -Force -ErrorAction SilentlyContinue
    try {
        Invoke-Ssh "rm -rf '$RemoteRunDir/auth.sh' '$RemoteRunDir/sequence.sh' '$RemoteRunDir/tokens.env'"
    }
    catch {
        Write-Warning "Remote smoke cleanup did not complete: $($_.Exception.Message)"
    }
}
