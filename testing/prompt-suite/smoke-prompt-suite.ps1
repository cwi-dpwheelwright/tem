[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateScript({ Test-Path -LiteralPath $_ })]
    [string]$QuestionsPath,
    [string]$ValidationCriteriaPath,
    [switch]$SkipValidation,
    [switch]$AllowValidationFailures
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$FeatureName = "prompt-suite"
$RepoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..")).Path
if ([string]::IsNullOrWhiteSpace($QuestionsPath)) {
    $QuestionsPath = Join-Path $PSScriptRoot "everyday-framework-prompts.json"
}

$InputPath = (Resolve-Path -LiteralPath $QuestionsPath).Path
$SuiteName = [System.IO.Path]::GetFileNameWithoutExtension($InputPath)
$SuiteSlug = ($SuiteName.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
$RunId = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$RunName = "$RunId-$SuiteSlug"
$RunRoot = Join-Path $RepoRoot "testing\runs\$FeatureName\$RunName"
$AskOut = Join-Path $RunRoot "ask"
$ReportPath = Join-Path $RunRoot "report.md"
$AnalysisPath = Join-Path $RunRoot "analysis.json"
$ResponsesPath = Join-Path $RunRoot "responses.json"
$ValidationPath = Join-Path $RunRoot "validation.json"
if ([string]::IsNullOrWhiteSpace($ValidationCriteriaPath)) {
    $ValidationCriteriaPath = Join-Path $PSScriptRoot "validation-criteria.json"
}
$ResolvedValidationCriteriaPath = $null
if (-not $SkipValidation) {
    $ResolvedValidationCriteriaPath = (Resolve-Path -LiteralPath $ValidationCriteriaPath).Path
}

$PublicBaseUrl = if ($env:CWI_SMOKE_PUBLIC_BASE_URL) { $env:CWI_SMOKE_PUBLIC_BASE_URL.TrimEnd("/") } else { "https://cwigpt.com" }
$SshTarget = if ($env:CWI_SMOKE_SSH_TARGET) { $env:CWI_SMOKE_SSH_TARGET } else { "root@100.120.208.51" }
$IdentityBaseUrl = if ($env:CWI_SMOKE_IDENTITY_BASE_URL) { $env:CWI_SMOKE_IDENTITY_BASE_URL.TrimEnd("/") } else { "http://127.0.0.1:8085" }
$RemoteRoot = if ($env:CWI_SMOKE_REMOTE_ROOT) { $env:CWI_SMOKE_REMOTE_ROOT.TrimEnd("/") } else { "/tmp/cwi-smoke" }
$RemoteRunDir = "$RemoteRoot/$FeatureName/$RunName"
$BrowserUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CWI-Smoke/1.0"

$TechnicalLeakMarkers = @(
    "activated user memory",
    "governed memory",
    "memory_graph",
    "source_grounded",
    "routing to",
    "trace:",
    "from the available context",
    "current prompt asks",
    "governed intelligence platform",
    "got you - something broke",
    "something broke",
    "synced memory",
    "graph data",
    "no current priorities were found",
    "no current risk was found",
    "proactive intelligence"
)
$MisrouteMarkers = @(
    "clarify whether",
    "personal contact",
    "public figure",
    "someone related to my work",
    "I do not currently have enough information to identify who"
)
$GenericFallbackMarkers = @(
    "I found account context, but I could not turn it into a clean summary",
    "I don't have any stored memory about you yet"
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

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $content = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($resolvedPath))
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
    param($Object, [Parameter(Mandatory = $true)][string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function ConvertTo-StringArray {
    param($Value)
    if ($null -eq $Value) { return @() }
    if ($Value -is [string]) { return @($Value) }
    if ($Value -is [System.Collections.IEnumerable]) {
        return @($Value | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ })
    }

    return @([string]$Value)
}

function Get-IntCriteriaValue {
    param($Criteria, [Parameter(Mandatory = $true)][string]$Name, [int]$DefaultValue)
    $value = Get-PropertyValue -Object $Criteria -Name $Name
    if ($null -eq $value -or "$value" -eq "") {
        return $DefaultValue
    }

    return [int]$value
}

function Get-CollectionCount {
    param($Value)
    if ($null -eq $Value) {
        return 0
    }

    if ($Value -is [string]) {
        return 1
    }

    if ($Value -is [System.Collections.ICollection]) {
        return $Value.Count
    }

    return @($Value).Count
}

function Get-AnswerText {
    param($Response)
    $markdown = Get-PropertyValue -Object $Response -Name "answer_markdown"
    if ($null -ne $markdown) { return [string]::Join("`n", @($markdown | ForEach-Object { [string]$_ })) }
    $answer = Get-PropertyValue -Object $Response -Name "answer"
    if ($null -ne $answer) { return [string]::Join("`n", @($answer | ForEach-Object { [string]$_ })) }
    return ($Response | ConvertTo-Json -Depth 50)
}

function Invoke-CheckedProcess {
    param([Parameter(Mandatory = $true)][string]$FilePath, [Parameter(Mandatory = $true)][string[]]$Arguments)
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

function Wait-PublicReady {
    param(
        [Parameter(Mandatory = $true)][string]$BaseUrl,
        [int]$TimeoutSeconds = 120
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $lastError = $null
    do {
        try {
            $ready = Invoke-RestMethod -Uri "$BaseUrl/api/ready" -Method Get -TimeoutSec 10
            Write-JsonFile -Path (Join-Path $RunRoot "preflight-ready.json") -Value $ready
            if ((Get-PropertyValue -Object $ready -Name "ready") -eq $true) {
                return
            }

            $lastError = "ready endpoint returned ready=false"
        }
        catch {
            $lastError = $_.Exception.Message
        }

        Start-Sleep -Seconds 5
    } while ((Get-Date) -lt $deadline)

    throw "Public API readiness did not become healthy within $TimeoutSeconds seconds. Last error: $lastError"
}

function Write-TempScript {
    param([Parameter(Mandatory = $true)][string]$Name, [Parameter(Mandatory = $true)][string]$Content)
    $path = Join-Path ([System.IO.Path]::GetTempPath()) $Name
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText($path, $Content, $utf8NoBom)
    return $path
}

function Test-ContainsAny {
    param([Parameter(Mandatory = $true)][string]$Text, [Parameter(Mandatory = $true)][string[]]$Markers)
    foreach ($marker in $Markers) {
        if ($Text.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }
    return $false
}

function Test-ContainsTechnicalLeak {
    param([Parameter(Mandatory = $true)][string]$Text)
    if (Test-ContainsAny -Text $Text -Markers $TechnicalLeakMarkers) {
        return $true
    }

    return [regex]::IsMatch($Text, "(?im)^\s*pack\s*:")
}

function Test-LooksLikeSendableDraft {
    param([Parameter(Mandatory = $true)][string]$Text)
    return Test-ContainsAny -Text $Text -Markers @(
        "Draft message:",
        "Draft:",
        "Message:",
        "Text:",
        "You can say",
        "You could say",
        "Try:",
        '"',
        "Hi ",
        "Hey ",
        "I'm ",
        "I am ",
        "I can't",
        "I made",
        "I think",
        "I felt",
        "I forgot",
        "I misunderstood",
        "I need some",
        "Please let",
        "Are you",
        "Is that",
        "I wanted to",
        "Let's",
        "Thanks",
        "Could you",
        "Would you",
        "Can we"
    )
}

function Test-HasPracticalOrder {
    param([Parameter(Mandatory = $true)][string]$Text)
    return Test-ContainsAny -Text $Text -Markers @(
        "first",
        "start with",
        "start by",
        "single action",
        "then",
        "next",
        "after",
        "priority",
        "prioritize",
        "urgent",
        "important",
        "1.",
        "2.",
        "order",
        "choose"
    )
}

function Test-LooksTruncated {
    param([Parameter(Mandatory = $true)][string]$Text)
    $trimmed = $Text.Trim()
    if ($trimmed.Length -lt 700) {
        return $false
    }

    if ($trimmed.EndsWith('```', [StringComparison]::Ordinal)) {
        return $false
    }

    return -not ([regex]::IsMatch($trimmed, '[.!?)]\s*$'))
}

function Get-TraceSummary {
    param($Response)
    $trace = Get-PropertyValue -Object $Response -Name "trace"
    $citations = Get-PropertyValue -Object $Response -Name "citations"
    $responseSources = Get-PropertyValue -Object $Response -Name "sources"
    $responseWarnings = Get-PropertyValue -Object $Response -Name "warnings"
    $unsupportedClaims = Get-PropertyValue -Object $Response -Name "unsupported_claims"
    $traceSources = Get-PropertyValue -Object $trace -Name "sources"
    $sourceResults = Get-PropertyValue -Object $trace -Name "source_results"
    $memoryResults = Get-PropertyValue -Object $trace -Name "memory_results"
    $homeNodeSources = Get-PropertyValue -Object $Response -Name "home_node_sources"
    return [pscustomobject]@{
        request_id = Get-PropertyValue -Object $trace -Name "request_id"
        trace_id = Get-PropertyValue -Object $trace -Name "trace_id"
        route = Get-PropertyValue -Object $trace -Name "route"
        intent_type = Get-PropertyValue -Object $trace -Name "intent_type"
        query_classification = Get-PropertyValue -Object $trace -Name "query_classification"
        template_used = Get-PropertyValue -Object $trace -Name "template_used"
        comprehension_expression_version = Get-PropertyValue -Object $trace -Name "comprehension_expression_version"
        context_awareness_mode = Get-PropertyValue -Object $trace -Name "context_awareness_mode"
        expression_tone_profile = Get-PropertyValue -Object $trace -Name "expression_tone_profile"
        presentation_framework_version = Get-PropertyValue -Object $trace -Name "presentation_framework_version"
        presentation_template_family = Get-PropertyValue -Object $trace -Name "presentation_template_family"
        presentation_formatting_mode = Get-PropertyValue -Object $trace -Name "presentation_formatting_mode"
        chat_response_quality_version = Get-PropertyValue -Object $trace -Name "chat_response_quality_version"
        chat_response_profile = Get-PropertyValue -Object $trace -Name "chat_response_profile"
        confidence = Get-PropertyValue -Object $trace -Name "confidence"
        confidence_level = Get-PropertyValue -Object $trace -Name "confidence_level"
        grounded = Get-PropertyValue -Object $trace -Name "grounded"
        evidence_required = Get-PropertyValue -Object $trace -Name "evidence_required"
        policy_mode = Get-PropertyValue -Object $trace -Name "policy_mode"
        model = Get-PropertyValue -Object $trace -Name "model"
        pack_hash = Get-PropertyValue -Object $trace -Name "pack_hash"
        memory_used = Get-PropertyValue -Object $trace -Name "memory_used"
        graph_used = Get-PropertyValue -Object $trace -Name "graph_used"
        pack_used = Get-PropertyValue -Object $trace -Name "pack_used"
        truth_state = Get-PropertyValue -Object $trace -Name "truth_state"
        truth_conflict_count = Get-PropertyValue -Object $trace -Name "truth_conflict_count"
        truth_evidence_count = Get-PropertyValue -Object $trace -Name "truth_evidence_count"
        execution_state = Get-PropertyValue -Object $Response -Name "execution_state"
        execution_target = Get-PropertyValue -Object $Response -Name "execution_target"
        home_node_id = Get-PropertyValue -Object $trace -Name "home_node_id"
        home_node_status = Get-PropertyValue -Object $trace -Name "home_node_status"
        home_node_execution_mode = Get-PropertyValue -Object $trace -Name "home_node_execution_mode"
        source_groups = [pscustomobject]@{
            response_sources = Get-CollectionCount -Value $responseSources
            trace_sources = Get-CollectionCount -Value $traceSources
            citations = Get-CollectionCount -Value $citations
            source_results = Get-CollectionCount -Value $sourceResults
            memory_results = Get-CollectionCount -Value $memoryResults
            document_hits = Get-PropertyValue -Object $trace -Name "document_hits"
            graph_hits = Get-PropertyValue -Object $trace -Name "graph_hits"
            trusted_web_hits = Get-PropertyValue -Object $trace -Name "trusted_web_hits"
            home_node_sources = Get-CollectionCount -Value $homeNodeSources
        }
        response_quality = [pscustomobject]@{
            warnings = Get-CollectionCount -Value $responseWarnings
            unsupported_claims = Get-CollectionCount -Value $unsupportedClaims
        }
    }
}

function Get-NormalizedEvaluationStatus {
    param([Parameter(Mandatory = $true)][string]$Verdict)
    switch ($Verdict) {
        "VALID" { return "valid" }
        "NEEDS_REVIEW" { return "needs_work" }
        "NEEDS_FIX" { return "failed" }
        "DEPENDENCY_UNAVAILABLE" { return "failed" }
        default { return "needs_work" }
    }
}

function Get-IssueFrameworkCategory {
    param([Parameter(Mandatory = $true)][string]$Issue)
    switch -Regex ($Issue) {
        "memory|context_fallback|context|priority|reminder" { return "memory" }
        "misroute|route|entity_lookup" { return "routing" }
        "technical|backend|truncated|format|starting_point_only" { return "presentation" }
        "source|weather|evidence|ground" { return "source_use" }
        "security|wallet|bank|card|phishing|password|safety|suspicious" { return "safety" }
        "api|dependency|http|timeout|unavailable" { return "transport" }
        "message|draft|invented|practical|order|priority|clarification|generic|answer|criteria|assumption|food|milk|chicken|payback|apology|birthday|ride|late_bus|extension" {
            return "comprehension_expression"
        }
        default { return "comprehension_expression" }
    }
}

function New-EvaluationSummary {
    param([Parameter(Mandatory = $true)]$Evaluation)
    $issues = @($Evaluation.issues | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $categories = @($issues | ForEach-Object { Get-IssueFrameworkCategory -Issue $_ } | Select-Object -Unique)
    if ($categories.Count -eq 0 -and $Evaluation.verdict -eq "VALID") {
        $categories = @()
    }

    return [pscustomobject]@{
        verdict = $Evaluation.verdict
        status = Get-NormalizedEvaluationStatus -Verdict $Evaluation.verdict
        issues = $issues
        framework_categories = $categories
        rationale = $Evaluation.rationale
    }
}

function Format-SourceGroups {
    param($SourceGroups)
    if ($null -eq $SourceGroups) {
        return "none"
    }

    $parts = @()
    foreach ($name in @("response_sources", "trace_sources", "citations", "source_results", "memory_results", "document_hits", "graph_hits", "trusted_web_hits", "home_node_sources")) {
        $value = Get-PropertyValue -Object $SourceGroups -Name $name
        if ($null -ne $value -and "$value" -ne "") {
            $parts += "$name=$value"
        }
    }

    if ($parts.Count -eq 0) {
        return "none"
    }

    return ($parts -join ", ")
}

function Evaluate-Answer {
    param(
        [Parameter(Mandatory = $true)]$Question,
        [Parameter(Mandatory = $true)][string]$Answer,
        [Parameter(Mandatory = $true)]$Trace,
        [Parameter(Mandatory = $true)][int]$HttpStatus
    )

    $issues = New-Object System.Collections.Generic.List[string]
    if ($HttpStatus -ge 500 -or $Answer.IndexOf("dependency_unavailable", [StringComparison]::OrdinalIgnoreCase) -ge 0 -or $Answer.IndexOf("error code: 524", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        return [pscustomobject]@{
            verdict = "DEPENDENCY_UNAVAILABLE"
            issues = @("api_dependency_unavailable")
            rationale = "The API dependency was unavailable, so the response cannot be evaluated as a framework behavior."
        }
    }

    if ([string]::IsNullOrWhiteSpace($Answer) -or $Answer.Trim().Length -lt 20) {
        $issues.Add("empty_or_too_short")
    }

    if (Test-ContainsTechnicalLeak -Text $Answer) {
        $issues.Add("technical_or_backend_language_leaked")
    }

    if (Test-LooksTruncated -Text $Answer) {
        $issues.Add("answer_appears_truncated")
    }

    if (Test-ContainsAny -Text $Answer -Markers $MisrouteMarkers) {
        $issues.Add("entity_lookup_misroute_language")
    }

    if ((Test-ContainsAny -Text $Answer -Markers $GenericFallbackMarkers) -and $Question.group -ne "current_context_and_memory") {
        $issues.Add("generic_memory_or_context_fallback")
    }

    $messageDraftNeedsSendableText = $Question.group -eq "message_drafting" -and $Trace.intent_type -ne "needs_clarification"
    if ($messageDraftNeedsSendableText -and -not (Test-LooksLikeSendableDraft -Text $Answer)) {
        $issues.Add("message_draft_should_include_sendable_text")
    }

    if ($Question.group -eq "message_drafting") {
        if ($Answer -match '(?i)\bfamily commitment\b' -and $Question.prompt -notmatch '(?i)\bfamily\b') {
            $issues.Add("message_draft_invented_reason")
        }

        $containsCalendarDate = $Answer -match '(?i)\b(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:tember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\s+\d{1,2}(?:st|nd|rd|th)?\b'
        $promptContainsCalendarDate = $Question.prompt -match '(?i)\b(?:jan(?:uary)?|feb(?:ruary)?|mar(?:ch)?|apr(?:il)?|may|jun(?:e)?|jul(?:y)?|aug(?:ust)?|sep(?:tember)?|oct(?:ober)?|nov(?:ember)?|dec(?:ember)?)\s+\d{1,2}(?:st|nd|rd|th)?\b'
        if ($containsCalendarDate -and -not $promptContainsCalendarDate) {
            $issues.Add("message_draft_invented_date")
        }

        $promptAsksForMissingMessyExplanation = $Question.prompt -match '(?i)messy explanation'
        $answerRequestsOrTemplatesMissingText = $Answer -match '(?i)\[(?:details|context|main point|message|explanation)\]|send me|paste|share|what you want to say|fill|one more detail|what exactly|what should'
        if ($promptAsksForMissingMessyExplanation -and -not $answerRequestsOrTemplatesMissingText) {
            $issues.Add("missing_input_should_not_invent_message_content")
        }
    }

    if (($Question.group -in @("budget_prioritization", "decision_support", "practical_prioritization", "time_boxed_planning")) -and (-not (Test-HasPracticalOrder -Text $Answer))) {
        $issues.Add("practical_reasoning_should_include_clear_order_or_priority")
    }

    if ($Question.prompt -match '(?i)starting point only' -and ($Answer.Length -gt 320 -or $Answer -match '(?i)\b(next|then|also|after that)\b')) {
        $issues.Add("starting_point_only_prompt_should_stay_single_action")
    }

    if (($Question.group -eq "security_and_financial_safety") -and (-not (Test-ContainsAny -Text $Answer -Markers @("link", "sender", "official", "password", "phishing", "suspicious", "2FA", "two-factor", "do not click", "don't click", "report")))) {
        $issues.Add("security_answer_should_include_verification_or_safety_step")
    }

    if (($Question.group -eq "verification_without_assumption") -and (-not (Test-ContainsAny -Text $Answer -Markers @("check", "verify", "ask", "confirm", "list", "schedule", "message")))) {
        $issues.Add("verification_prompt_should_avoid_assumption_and_check_source")
    }

    switch ($Question.id) {
        "food_safety_001" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("3 to 4", "3-4", "three to four"))) {
                $issues.Add("cooked_chicken_answer_missing_3_to_4_day_guidance")
            }
        }
        "food_safety_002" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("when in doubt", "throw", "discard", "smell", "expiration", "date", "sour"))) {
                $issues.Add("milk_safety_answer_missing_caution_or_checks")
            }
        }
        "lost_wallet_001" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("freeze", "cancel", "bank", "card", "ID", "report"))) {
                $issues.Add("lost_wallet_answer_missing_immediate_security_steps")
            }
        }
        "weather_001" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("location", "city", "where are you", "forecast", "temperature", "weather provider", " wttr.in", " F "))) {
                $issues.Add("weather_answer_did_not_ask_location_or_show_weather")
            }
        }
        "memory_001" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("don't have", "do not have", "no stored", "no reminders", "tell me", "today"))) {
                $issues.Add("reminder_answer_should_be_clear_empty_state_or_actionable")
            }
        }
        "buying_001" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("what", "depends", "wait", "need", "budget", "urgent", "price", "condition", "battery", "warranty", "return"))) {
                $issues.Add("buy_now_answer_should_ask_context_or_give_decision_criteria")
            }
        }
        "message_001" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("sorry", "cancel", "can't make", "reschedule", "plans"))) {
                $issues.Add("cancel_plans_should_include_sendable_text")
            }
        }
        "message_002" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("ride", "could", "would", "please", "if you can"))) {
                $issues.Add("ride_request_should_include_polite_sendable_text")
            }
        }
        "message_003" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("late", "missed", "bus", "update", "arrival"))) {
                $issues.Add("late_bus_text_should_include_sendable_text")
            }
        }
        "message_004" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("birthday", "sorry", "hope", "belated"))) {
                $issues.Add("forgot_birthday_should_include_sendable_text")
            }
        }
        "message_005" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("sorry", "apologize", "shouldn't", "I was wrong"))) {
                $issues.Add("apology_should_include_brief_accountable_text")
            }
        }
        "message_006" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("pay", "money", "send", "when you can", "owe", "outstanding amount", "settle", "reimburse", "repay"))) {
                $issues.Add("payback_request_should_include_polite_sendable_text")
            }
        }
        "message_007" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("thank", "appreciate", "means a lot"))) {
                $issues.Add("thank_you_should_include_sendable_text")
            }
        }
        "message_008" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("clarify", "help me understand", "could you", "manager", "teacher"))) {
                $issues.Add("clarification_request_should_include_sendable_text")
            }
        }
        "message_009" {
            if (-not (Test-ContainsAny -Text $Answer -Markers @("missed", "sorry", "my responsibility", "will", "make it right"))) {
                $issues.Add("missed_something_explanation_should_avoid_excuses")
            }
        }
    }

    if ($Question.group -notin @("current_context_and_memory") -and $Trace.memory_used -eq $true) {
        $issues.Add("unexpected_memory_use_for_general_life_prompt")
    }

    if ($issues.Count -eq 0) {
        return [pscustomobject]@{ verdict = "VALID"; issues = @(); rationale = "Actionable user-facing answer with no obvious framework leakage." }
    }

    $severity = if ($issues -contains "api_dependency_unavailable" -or $issues -contains "entity_lookup_misroute_language" -or $issues -contains "technical_or_backend_language_leaked") { "NEEDS_FIX" } else { "NEEDS_REVIEW" }
    return [pscustomobject]@{ verdict = $severity; issues = @($issues); rationale = "One or more behavioral checks flagged the response." }
}

function New-AskRecord {
    param([Parameter(Mandatory = $true)][System.IO.DirectoryInfo]$Directory, [Parameter(Mandatory = $true)]$Question)
    $meta = Read-JsonIfPresent -Path (Join-Path $Directory.FullName "meta.json")
    $response = Read-JsonIfPresent -Path (Join-Path $Directory.FullName "response.json")
    $answer = Get-AnswerText $response
    $trace = Get-TraceSummary $response
    $httpStatus = [int]$meta.http_status
    $evaluationArgs = @{
        Question = $Question
        Answer = [string]$answer
        Trace = $trace
        HttpStatus = $httpStatus
    }
    $evaluation = New-EvaluationSummary -Evaluation (Evaluate-Answer @evaluationArgs)
    return [pscustomobject]@{
        id = $Question.id
        group = $Question.group
        prompt = $Question.prompt
        http_status = $httpStatus
        answer = $answer
        trace = $trace
        evaluation = $evaluation
    }
}

function Write-Report {
    param([Parameter(Mandatory = $true)]$Analysis)
    $status = if ($Analysis.overall_verdict -eq "NEEDS_WORK") { "NEEDS WORK" } else { $Analysis.overall_verdict.Replace("_", " ") }
    $tick = [char]96
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Everyday Prompt Suite Smoke - $status")
    $lines.Add("")
    $lines.Add("- Run ID: ${tick}$RunName$tick")
    $lines.Add("- Smoke user: ${tick}$($Analysis.auth_context.smoke_user.username) / $($Analysis.auth_context.smoke_user.user_id)$tick")
    $lines.Add("- Prompt count: ${tick}$($Analysis.prompt_count)$tick")
    $lines.Add("- Ask endpoint: ${tick}/api/ask$tick")
    $lines.Add("")
    $lines.Add("## Summary")
    foreach ($row in $Analysis.summary_by_verdict) {
        $lines.Add("- $($row.verdict): ${tick}$($row.count)$tick")
    }
    $lines.Add("")
    $lines.Add("## Reportable Status")
    foreach ($row in $Analysis.summary_by_status) {
        $lines.Add("- $($row.status): ${tick}$($row.count)$tick")
    }
    $validation = Get-PropertyValue -Object $Analysis -Name "validation"
    if ($null -ne $validation) {
        $lines.Add("")
        $lines.Add("## Validation Gate")
        $lines.Add("- Status: ${tick}$($validation.status)$tick")
        $lines.Add("- Criteria: ${tick}$($validation.criteria_path)$tick")
        if (@($validation.failures).Count -gt 0) {
            foreach ($failure in $validation.failures) {
                $lines.Add("- Failure: ${tick}$failure$tick")
            }
        }
    }
    if (@($Analysis.summary_by_failure_category).Count -gt 0) {
        $lines.Add("")
        $lines.Add("## Framework Failure Categories")
        foreach ($row in $Analysis.summary_by_failure_category) {
            $lines.Add("- $($row.category): ${tick}$($row.count)$tick")
        }
    }
    $lines.Add("")
    $lines.Add("## Grouped Analysis")
    foreach ($group in $Analysis.group_summaries) {
        $lines.Add("### $($group.group)")
        $lines.Add("- Prompts: ${tick}$($group.count)$tick")
        $lines.Add("- Valid: ${tick}$($group.valid)$tick")
        $lines.Add("- Needs review/fix: ${tick}$($group.needs_work)$tick")
        foreach ($category in $group.common_failure_categories) {
            $lines.Add("- Category: ${tick}$category$tick")
        }
        foreach ($issue in $group.common_issues) {
            $lines.Add("- Issue: ${tick}$issue$tick")
        }
        $lines.Add("")
    }
    $lines.Add("## Responses")
    foreach ($record in $Analysis.records) {
        $lines.Add("### $($record.id) - $($record.group)")
        $lines.Add("- Status: ${tick}$($record.evaluation.status)$tick")
        $lines.Add("- Verdict: ${tick}$($record.evaluation.verdict)$tick")
        $lines.Add("- Categories: ${tick}$((@($record.evaluation.framework_categories) -join ', '))$tick")
        $lines.Add("- Issues: ${tick}$((@($record.evaluation.issues) -join ', '))$tick")
        $lines.Add("- Confidence: ${tick}$($record.trace.confidence) / $($record.trace.confidence_level)$tick")
        $lines.Add("- Route: ${tick}$($record.trace.route)$tick")
        $lines.Add("- Intent: ${tick}$($record.trace.intent_type)$tick")
        $lines.Add("- Execution: ${tick}$($record.trace.execution_state) / $($record.trace.execution_target)$tick")
        $lines.Add("- Source groups: ${tick}$(Format-SourceGroups -SourceGroups $record.trace.source_groups)$tick")
        $lines.Add("- Context mode: ${tick}$($record.trace.context_awareness_mode)$tick")
        $lines.Add("- Presentation: ${tick}$($record.trace.presentation_template_family) / $($record.trace.presentation_formatting_mode)$tick")
        $lines.Add("- Chat profile: ${tick}$($record.trace.chat_response_profile)$tick")
        $lines.Add("- Memory used: ${tick}$($record.trace.memory_used)$tick")
        $lines.Add("")
        $lines.Add("Prompt:")
        $lines.Add('```text')
        $lines.Add($record.prompt)
        $lines.Add('```')
        $lines.Add("Answer:")
        $lines.Add('```text')
        $preview = if ($record.answer.Length -gt 1400) { $record.answer.Substring(0, 1400) + "..." } else { $record.answer }
        $lines.Add($preview)
        $lines.Add('```')
        $lines.Add("")
    }
    ($lines -join "`n") | Set-Content -LiteralPath $ReportPath -Encoding UTF8
}

function Build-Analysis {
    param([Parameter(Mandatory = $true)]$AuthContext, [Parameter(Mandatory = $true)]$Questions)
    $records = @()
    $index = 1
    foreach ($question in $Questions) {
        $dir = Get-ChildItem -LiteralPath $AskOut -Directory |
            Where-Object { $_.Name -like ("{0:D2}-*" -f $index) } |
            Select-Object -First 1
        $records += New-AskRecord -Directory $dir -Question $question
        $index++
    }

    $summaryByVerdict = @($records | Group-Object { $_.evaluation.verdict } | ForEach-Object {
        [pscustomobject]@{ verdict = $_.Name; count = $_.Count }
    })
    $summaryByStatus = @($records | Group-Object { $_.evaluation.status } | ForEach-Object {
        [pscustomobject]@{ status = $_.Name; count = $_.Count }
    })
    $summaryByFailureCategory = @($records |
        Where-Object { $_.evaluation.status -ne "valid" } |
        ForEach-Object { $_.evaluation.framework_categories } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Group-Object |
        Sort-Object Count -Descending |
        ForEach-Object { [pscustomobject]@{ category = $_.Name; count = $_.Count } })
    $groupSummaries = @($records | Group-Object group | ForEach-Object {
        $groupRecords = @($_.Group)
        $issues = @($groupRecords | ForEach-Object { $_.evaluation.issues } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        $categories = @($groupRecords | ForEach-Object { $_.evaluation.framework_categories } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        [pscustomobject]@{
            group = $_.Name
            count = $groupRecords.Count
            valid = @($groupRecords | Where-Object { $_.evaluation.verdict -eq "VALID" }).Count
            needs_work = @($groupRecords | Where-Object { $_.evaluation.verdict -ne "VALID" }).Count
            common_failure_categories = @($categories | Group-Object | Sort-Object Count -Descending | Select-Object -First 5 | ForEach-Object { "$($_.Name) ($($_.Count))" })
            common_issues = @($issues | Group-Object | Sort-Object Count -Descending | Select-Object -First 5 | ForEach-Object { "$($_.Name) ($($_.Count))" })
        }
    })
    $needsFixCount = @($records | Where-Object { $_.evaluation.verdict -eq "NEEDS_FIX" }).Count
    $needsReviewCount = @($records | Where-Object { $_.evaluation.verdict -eq "NEEDS_REVIEW" }).Count
    $dependencyUnavailableCount = @($records | Where-Object { $_.evaluation.verdict -eq "DEPENDENCY_UNAVAILABLE" }).Count
    $overall = if ($dependencyUnavailableCount -gt 0) {
        "INFRA_BLOCKED"
    }
    elseif ($needsFixCount -gt 0) {
        "NEEDS_WORK"
    }
    elseif ($needsReviewCount -gt 0) {
        "PASS_WITH_REVIEW_ITEMS"
    }
    else {
        "PASS"
    }

    return [pscustomobject]@{
        run_id = $RunName
        feature = $FeatureName
        auth_context = $AuthContext
        prompt_count = $Questions.Count
        summary_by_verdict = $summaryByVerdict
        summary_by_status = $summaryByStatus
        summary_by_failure_category = $summaryByFailureCategory
        group_summaries = $groupSummaries
        records = $records
        overall_verdict = $overall
    }
}

function Test-AnalysisAgainstCriteria {
    param(
        [Parameter(Mandatory = $true)]$Analysis,
        [Parameter(Mandatory = $true)]$Criteria,
        [Parameter(Mandatory = $true)][string]$CriteriaPath
    )

    $failures = New-Object System.Collections.Generic.List[string]
    $records = @($Analysis.records)
    $allowedOverallVerdicts = ConvertTo-StringArray -Value (Get-PropertyValue -Object $Criteria -Name "allowed_overall_verdicts")
    if ($allowedOverallVerdicts.Count -gt 0 -and $allowedOverallVerdicts -notcontains $Analysis.overall_verdict) {
        $failures.Add("overall_verdict_not_allowed:$($Analysis.overall_verdict)")
    }

    $failedCount = @($records | Where-Object { $_.evaluation.status -eq "failed" }).Count
    $needsWorkCount = @($records | Where-Object { $_.evaluation.status -eq "needs_work" }).Count
    $needsFixCount = @($records | Where-Object { $_.evaluation.verdict -eq "NEEDS_FIX" }).Count
    $dependencyUnavailableCount = @($records | Where-Object { $_.evaluation.verdict -eq "DEPENDENCY_UNAVAILABLE" }).Count

    $maxFailed = Get-IntCriteriaValue -Criteria $Criteria -Name "max_failed" -DefaultValue 0
    $maxNeedsWork = Get-IntCriteriaValue -Criteria $Criteria -Name "max_needs_work" -DefaultValue 0
    $maxNeedsFix = Get-IntCriteriaValue -Criteria $Criteria -Name "max_needs_fix" -DefaultValue 0
    $maxDependencyUnavailable = Get-IntCriteriaValue -Criteria $Criteria -Name "max_dependency_unavailable" -DefaultValue 0
    $minPromptCount = Get-IntCriteriaValue -Criteria $Criteria -Name "min_prompt_count" -DefaultValue 1

    if ($records.Count -lt $minPromptCount) {
        $failures.Add("prompt_count_below_minimum:$($records.Count)<$minPromptCount")
    }

    if ($failedCount -gt $maxFailed) {
        $failures.Add("failed_count_exceeded:$failedCount>$maxFailed")
    }

    if ($needsWorkCount -gt $maxNeedsWork) {
        $failures.Add("needs_work_count_exceeded:$needsWorkCount>$maxNeedsWork")
    }

    if ($needsFixCount -gt $maxNeedsFix) {
        $failures.Add("needs_fix_count_exceeded:$needsFixCount>$maxNeedsFix")
    }

    if ($dependencyUnavailableCount -gt $maxDependencyUnavailable) {
        $failures.Add("dependency_unavailable_count_exceeded:$dependencyUnavailableCount>$maxDependencyUnavailable")
    }

    $blockedIssues = ConvertTo-StringArray -Value (Get-PropertyValue -Object $Criteria -Name "blocked_issues")
    foreach ($record in $records) {
        $issues = ConvertTo-StringArray -Value $record.evaluation.issues
        foreach ($issue in $issues) {
            if ($blockedIssues -contains $issue) {
                $failures.Add("blocked_issue:$($record.id):$issue")
            }
        }
    }

    $requiredTraceFields = ConvertTo-StringArray -Value (Get-PropertyValue -Object $Criteria -Name "required_trace_fields")
    foreach ($record in $records) {
        foreach ($field in $requiredTraceFields) {
            $value = Get-PropertyValue -Object $record.trace -Name $field
            if ($null -eq $value -or ([string]$value).Trim() -eq "") {
                $failures.Add("missing_trace_field:$($record.id):$field")
            }
        }
    }

    $requiredRecordFields = ConvertTo-StringArray -Value (Get-PropertyValue -Object $Criteria -Name "required_record_fields")
    foreach ($record in $records) {
        foreach ($field in $requiredRecordFields) {
            $value = Get-PropertyValue -Object $record -Name $field
            if ($null -eq $value -or ([string]$value).Trim() -eq "") {
                $failures.Add("missing_record_field:$($record.id):$field")
            }
        }
    }

    $status = if ($failures.Count -eq 0) { "pass" } else { "fail" }
    return [pscustomobject]@{
        schema_version = "cwi.prompt-suite.validation-result.v1"
        status = $status
        criteria_path = $CriteriaPath
        run_id = $Analysis.run_id
        prompt_count = $records.Count
        counts = [pscustomobject]@{
            failed = $failedCount
            needs_work = $needsWorkCount
            needs_fix = $needsFixCount
            dependency_unavailable = $dependencyUnavailableCount
        }
        failures = @($failures)
    }
}

New-Directory $RunRoot
New-Directory $AskOut
Copy-Item -LiteralPath $InputPath -Destination (Join-Path $RunRoot "questions.json") -Force
if (-not $SkipValidation) {
    Copy-Item -LiteralPath $ResolvedValidationCriteriaPath -Destination (Join-Path $RunRoot "validation-criteria.json") -Force
}
Wait-PublicReady -BaseUrl $PublicBaseUrl

$authScript = @'
#!/usr/bin/env bash
set -Eeuo pipefail
REMOTE_RUN_DIR="${1:?remote run dir required}"
IDENTITY_BASE="${2:?identity base url required}"
mkdir -p "$REMOTE_RUN_DIR/ask"
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
    curl -fsS "$IDENTITY_BASE/auth/login" -H 'Content-Type: application/json' --data-binary @- |
    jq -er '.access_token'
}

ADMIN_TOKEN="$(login "$ADMIN_TENANT" "$ADMIN_USERNAME" "$ADMIN_PASSWORD")"
RUN_SUFFIX="$(date +%s%N)"
SMOKE_USERNAME="prompt-suite-smoke-$RUN_SUFFIX"
SMOKE_EMAIL="$SMOKE_USERNAME@smoke.invalid"
SMOKE_PASSWORD="SmokeT3st!$RUN_SUFFIX"

jq -nc --arg tenant_id "$ADMIN_TENANT" --arg username "$SMOKE_USERNAME" --arg email "$SMOKE_EMAIL" --arg display_name "Prompt Suite Smoke" \
  '{tenant_id:$tenant_id,username:$username,email:$email,display_name:$display_name}' |
  curl -fsS "$IDENTITY_BASE/admin/users/invite" \
    -H "Authorization: Bearer $ADMIN_TOKEN" -H 'Content-Type: application/json' --data-binary @- >/dev/null

SMOKE_USER_ID="$(curl -fsS "$IDENTITY_BASE/admin/users?tenant_id=$(printf '%s' "$ADMIN_TENANT" | jq -sRr @uri)" \
  -H "Authorization: Bearer $ADMIN_TOKEN" |
  jq -er --arg email "$SMOKE_EMAIL" '.[] | select(.email == $email) | .user_id' | head -n 1)"

jq -nc --arg tenant_id "$ADMIN_TENANT" '{tenant_id:$tenant_id}' |
  curl -fsS "$IDENTITY_BASE/admin/users/$SMOKE_USER_ID/approve" \
    -H "Authorization: Bearer $ADMIN_TOKEN" -H 'Content-Type: application/json' --data-binary @- >/dev/null

jq -nc --arg tenant_id "$ADMIN_TENANT" --arg password "$SMOKE_PASSWORD" '{tenant_id:$tenant_id,password:$password}' |
  curl -fsS "$IDENTITY_BASE/admin/users/$SMOKE_USER_ID/password" \
    -H "Authorization: Bearer $ADMIN_TOKEN" -H 'Content-Type: application/json' --data-binary @- >/dev/null

SMOKE_TOKEN="$(login "$ADMIN_TENANT" "$SMOKE_USERNAME" "$SMOKE_PASSWORD")"

jq -n --arg tenant_id "$ADMIN_TENANT" --arg username "$SMOKE_USERNAME" --arg user_id "$SMOKE_USER_ID" \
  '{tenant_id:$tenant_id,smoke_user:{username:$username,user_id:$user_id}}' > "$REMOTE_RUN_DIR/auth-context.json"
{
  printf 'TENANT_ID=%q\n' "$ADMIN_TENANT"
  printf 'SMOKE_USERNAME=%q\n' "$SMOKE_USERNAME"
  printf 'SMOKE_PASSWORD=%q\n' "$SMOKE_PASSWORD"
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
    jq -nc --arg tenant_id "$TENANT_ID" --arg reason "prompt suite smoke cleanup" '{tenant_id:$tenant_id,reason:$reason}' |
      curl -fsS "$IDENTITY_BASE/admin/users/$SMOKE_USER_ID/remove" \
        -H "Authorization: Bearer $ADMIN_TOKEN" -H 'Content-Type: application/json' --data-binary @- >/dev/null || true
  fi
}
trap cleanup EXIT

login() {
  jq -nc --arg tenant_id "$TENANT_ID" --arg username "$SMOKE_USERNAME" --arg password "$SMOKE_PASSWORD" \
    '{tenant_id:$tenant_id,username:$username,password:$password}' |
    curl -fsS "$IDENTITY_BASE/auth/login" -H 'Content-Type: application/json' --data-binary @- |
    jq -er '.access_token'
}

slugify() {
  printf '%s' "$1" | tr '[:upper:]' '[:lower:]' | sed -E 's/[^a-z0-9]+/-/g; s/^-+//; s/-+$//' | cut -c1-48
}

i=0
jq -c '.[]' "$REMOTE_RUN_DIR/questions.json" | while read -r row; do
  i=$((i + 1))
  if [[ "$i" -eq 1 || $(( (i - 1) % 20 )) -eq 0 ]]; then
    SMOKE_TOKEN="$(login)"
  fi
  id="$(jq -r '.id' <<<"$row")"
  group="$(jq -r '.group' <<<"$row")"
  prompt="$(jq -r '.prompt' <<<"$row")"
  dir="$REMOTE_RUN_DIR/ask/$(printf '%02d' "$i")-$(slugify "$id")"
  mkdir -p "$dir"

  conversation_id="prompt-suite-$id"
  jq -n --arg tenant_id "$TENANT_ID" --arg user_id "$SMOKE_USER_ID" --arg question "$prompt" --arg conversation_id "$conversation_id" \
    '{tenant_id:$tenant_id,user_id:$user_id,question:$question,pack:{name:"personal"},context:{mode:"observe",top_k:8,timezone:"America/Chicago",conversation_id:$conversation_id}}' \
    > "$dir/payload.json"

  status="$(curl -sS -o "$dir/response.json" -w '%{http_code}' "$PUBLIC_BASE_URL/api/ask" \
    -H "Authorization: Bearer $SMOKE_TOKEN" \
    -H 'Accept: application/json' \
    -H 'Content-Type: application/json' \
    -H "User-Agent: $BROWSER_UA" \
    --data-binary "@$dir/payload.json")"

  jq -nc --arg id "$id" --arg group "$group" --arg prompt "$prompt" --arg status "$status" \
    '{id:$id,group:$group,prompt:$prompt,payload_file:"payload.json",response_file:"response.json",http_status:($status|tonumber)}' \
    > "$dir/meta.json"
done
'@

$authScriptPath = Write-TempScript -Name "cwi-prompt-suite-auth-$RunName.sh" -Content $authScript
$sequenceScriptPath = Write-TempScript -Name "cwi-prompt-suite-sequence-$RunName.sh" -Content $sequenceScript

try {
    Invoke-Ssh "mkdir -p '$RemoteRunDir/ask'"
    Invoke-Scp -Arguments @($authScriptPath, "${SshTarget}:$RemoteRunDir/auth.sh")
    Invoke-Ssh "chmod 700 '$RemoteRunDir/auth.sh' && '$RemoteRunDir/auth.sh' '$RemoteRunDir' '$IdentityBaseUrl'"
    Invoke-Scp -Arguments @("${SshTarget}:$RemoteRunDir/auth-context.json", (Join-Path $RunRoot "auth-context.json"))
    Invoke-Scp -Arguments @($InputPath, "${SshTarget}:$RemoteRunDir/questions.json")
    Invoke-Scp -Arguments @($sequenceScriptPath, "${SshTarget}:$RemoteRunDir/sequence.sh")
    Invoke-Ssh "chmod 700 '$RemoteRunDir/sequence.sh' && '$RemoteRunDir/sequence.sh' '$RemoteRunDir' '$PublicBaseUrl' '$BrowserUserAgent' '$IdentityBaseUrl'"
    Invoke-Scp -Arguments @("-r", "${SshTarget}:$RemoteRunDir/ask/.", $AskOut)

    $authContext = Read-JsonIfPresent -Path (Join-Path $RunRoot "auth-context.json")
    $questionsRaw = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($InputPath)) | ConvertFrom-Json
    $questions = @()
    foreach ($question in $questionsRaw) {
        $questions += $question
    }
    $analysis = Build-Analysis -AuthContext $authContext -Questions $questions
    if (-not $SkipValidation) {
        $criteria = Read-JsonIfPresent -Path $ResolvedValidationCriteriaPath
        $validation = Test-AnalysisAgainstCriteria -Analysis $analysis -Criteria $criteria -CriteriaPath $ResolvedValidationCriteriaPath
        $analysis | Add-Member -NotePropertyName "validation" -NotePropertyValue $validation -Force
        Write-JsonFile -Path $ValidationPath -Value $validation
    }
    Write-JsonFile -Path $AnalysisPath -Value $analysis
    Write-JsonFile -Path $ResponsesPath -Value @($analysis.records | ForEach-Object {
        [pscustomobject]@{
            id = $_.id
            group = $_.group
            prompt = $_.prompt
            answer = $_.answer
            http_status = $_.http_status
            status = $_.evaluation.status
            verdict = $_.evaluation.verdict
            issues = $_.evaluation.issues
            framework_categories = $_.evaluation.framework_categories
            trace = $_.trace
        }
    })
    Write-Report -Analysis $analysis

    Write-Host "Run directory: $RunRoot"
    Write-Host "Report: $ReportPath"
    Write-Host "Responses: $ResponsesPath"
    Write-Host "Verdict: $($analysis.overall_verdict)"
    if (-not $SkipValidation) {
        Write-Host "Validation: $($analysis.validation.status)"
        Write-Host "Validation artifact: $ValidationPath"
        if ($analysis.validation.status -ne "pass" -and -not $AllowValidationFailures) {
            throw "Prompt-suite validation failed. See $ValidationPath"
        }
    }
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
