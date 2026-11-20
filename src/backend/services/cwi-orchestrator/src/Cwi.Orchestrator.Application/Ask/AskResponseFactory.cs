using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Application.Trace;
using Cwi.Orchestrator.Application.TrustedWeb;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;

namespace Cwi.Orchestrator.Application.Ask;

internal static class AskResponseFactory
{
    private static readonly Regex CitationMarkerRegex = new(@"(?<!\w)\[(\d{1,2})\]", RegexOptions.Compiled);

    public static AskResponseDto Create(
        AskWorkflowContext context,
        JourneyRequestContext journeyRequestContext,
        TraceLookupStore traceStore)
    {
        if (string.IsNullOrWhiteSpace(context.TraceId))
        {
            context.TraceId = Guid.NewGuid().ToString("N");
        }

        EnforceFinalAnswerPolicy(context);
        context.FinalAnswerHash = BuildFinalAnswerHash(context.Answer);
        var grounded = ResolveGrounded(context);
        var reasoningBased = ResolveReasoningBased(context, grounded);
        var confidence = CalculateConfidence(context, grounded, out var confidenceBasis);
        var confidenceLevel = ResolveConfidenceLevel(context, grounded, confidence);
        context.Grounded = grounded;
        context.ReasoningBased = reasoningBased;
        context.ConfidenceBasis = confidenceBasis;
        var useRetrievedEvidence = ShouldReportRetrievedEvidence(context);
        var sources = BuildSources(context);
        var citations = BuildCitations(context);
        var packsUsed = BuildPacksUsed(context);
        var warnings = context.ResponseWarnings.Count == 0
            ? null
            : context.ResponseWarnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var unsupportedClaims = BuildUnsupportedClaims(context);
        var requestId = context.RequestId ?? journeyRequestContext.RequestId;
        var trace = BuildTracePayload(
            context,
            requestId,
            sources,
            warnings,
            confidence,
            confidenceLevel,
            confidenceBasis,
            grounded,
            reasoningBased,
            useRetrievedEvidence);

        var response = new AskResponseDto
        {
            Answer = context.Answer ?? string.Empty,
            ExecutionState = DefaultIfBlank(context.ExecutionState, "completed"),
            ExecutionTarget = DefaultIfBlank(context.ExecutionTarget, "shared_compute"),
            DeferredWorkId = context.DeferredWorkId,
            HomeNodeStatus = context.HomeNodeStatus,
            ExecutionMessage = context.ExecutionMessage,
            HomeNodeSources = NullIfEmpty(context.HomeNodeSources),
            Citations = citations,
            IntentMode = context.IntentType,
            AudienceMode = context.AudienceMode,
            TemplateUsed = context.TemplateUsed,
            VerbosityMode = context.VerbosityMode,
            Confidence = confidence,
            ConfidenceLevel = confidenceLevel,
            PacksUsed = NullIfEmpty(packsUsed),
            Sources = NullIfEmpty(sources),
            Provenance = new AskResponseProvenanceDto
            {
                RequestId = requestId,
                TraceId = context.TraceId,
                GroundingMode = context.GroundingMode,
                Model = context.ModelName,
                PackHash = context.PackHash,
                PackVersion = context.PackVersion
            },
            Warnings = warnings,
            UnsupportedClaims = NullIfEmpty(unsupportedClaims),
            Trace = trace
        };

        var docId = useRetrievedEvidence ? context.Retrieved.FirstOrDefault()?.DocId : null;
        if (!string.IsNullOrWhiteSpace(docId))
        {
            traceStore.Record(context.TraceId, docId);
        }

        return response;
    }

    private static TracePayload BuildTracePayload(
        AskWorkflowContext context,
        string? requestId,
        IReadOnlyList<string> sources,
        IReadOnlyList<string>? warnings,
        float confidence,
        string? confidenceLevel,
        string confidenceBasis,
        bool grounded,
        bool reasoningBased,
        bool useRetrievedEvidence)
    {
        var communicationCore = ResolveCommunicationCore(context);

        return new TracePayload
        {
            RequestId = requestId,
            TraceId = context.TraceId!,
            Sources = sources,
            Confidence = confidence,
            ConfidenceLevel = confidenceLevel,
            ConfidenceBasis = confidenceBasis,
            RawPrompt = ResolveTraceRawPrompt(context),
            NormalizedPrompt = ResolveTraceNormalizedPrompt(context),
            NormalizationApplied = TrueOrNull(context.NormalizationApplied),
            NormalizationRules = NullIfEmpty(context.NormalizationRules),
            NormalizationFailed = TrueOrNull(context.NormalizationFailed),
            NormalizationFailureReason = ValueWhen(context.NormalizationFailed, context.NormalizationFailureReason),
            NormalizationConfidence = ResolveNormalizationConfidence(context),
            NormalizationConfidenceThreshold = ResolveNormalizationConfidenceThreshold(context),
            NormalizationRequiresVerification = ResolveNormalizationRequiresVerification(context),
            NormalizationVerificationMessage = ResolveNormalizationVerificationMessage(context),
            NormalizationVerificationCandidates = ResolveNormalizationVerificationCandidates(context),
            NormalizationCorrectionDecision = ResolveNormalizationCorrectionDecision(context),
            NormalizationCorrectionRisk = ResolveNormalizationCorrectionRisk(context),
            NormalizationCorrectionVisibility = ResolveNormalizationCorrectionVisibility(context),
            NormalizationCorrections = BuildNormalizationCorrectionTrace(context),
            GroundingMode = context.GroundingMode,
            Grounded = grounded,
            ReasoningBased = reasoningBased,
            Route = ResolveRoute(context),
            EntityFound = context.EntityFound,
            PackUsed = HasPackUsage(context),
            MemoryUsed = HasMemoryUsage(context),
            GraphUsed = context.GraphEvidence.Count > 0,
            RetrievalAllowed = context.RetrievalAllowed,
            RetrievalSkippedReason = context.RetrievalSkippedReason,
            RetrievalStatus = ResolveRetrievalStatus(context),
            RetrievalDomain = context.RetrievalDomain,
            RetrievalDomainTerms = NullIfEmpty(context.RetrievalDomainTerms),
            GraphHits = CountOrZero(useRetrievedEvidence, context.GraphEvidence.Count),
            DocumentHits = CountOrZero(useRetrievedEvidence, context.Retrieved.Count),
            MemoryResults = BuildMemoryTrace(context),
            MemoryFrameworkVersion = context.MemoryFrameworkVersion,
            MemoryActiveContextCount = CountWhen(context.MemoryFrameworkVersion is not null, context.MemoryActiveContextCount),
            MemoryExcludedContextCount = CountWhen(context.MemoryFrameworkVersion is not null, context.MemoryExcludedContextCount),
            MemoryFrameworkDecisions = BuildMemoryFrameworkDecisions(context),
            MemoryConflicts = BuildMemoryConflictTrace(context),
            SourceResults = BuildSourceTrace(context),
            MemoryUpdate = context.MemoryUpdate,
            MemoryUnavailable = TrueOrNull(context.MemoryUnavailable),
            SourceRetrievalUnavailable = TrueOrNull(context.SourceRetrievalUnavailable),
            TrustedWebRequested = TrueOrNull(context.TrustedWebContextRequested),
            TrustedWebEnabled = TrueOrNull(context.TrustedWebContextEnabled),
            TrustedWebProfile = context.TrustedWebContextProfile,
            TrustedWebHits = CountWhen(context.TrustedWebContextRequested, context.TrustedWebResults.Count),
            TrustedWebSkippedReason = context.TrustedWebContextSkippedReason,
            TrustedWebRetrievalDurationMs = context.TrustedWebContextRetrievalDurationMs,
            TrustedWebSources = BuildTrustedWebTrace(context),
            PolicyMode = context.PolicyMode,
            Model = context.ModelName ?? string.Empty,
            PackHash = context.PackHash ?? string.Empty,
            PackVersion = context.PackVersion ?? string.Empty,
            PackHashes = BuildPackHashes(context),
            PackCapabilities = BuildPackCapabilitySummary(context),
            PackCapabilityDecision = BuildPackCapabilityDecision(context),
            BaselinePacks = BuildBaselinePackSummary(context),
            CorePackOrder = NullIfEmpty(context.CorePackExecutionOrder),
            DomainPacks = NullIfEmpty(context.DomainPackNames),
            FinalLayers = NullIfEmpty(context.FinalLayerExecutionOrder),
            QueryClassification = context.QueryClassification,
            QueryClassificationReason = context.QueryClassificationReason,
            IntentType = context.IntentType,
            IntentMode = context.IntentType,
            AudienceMode = context.AudienceMode,
            VerbosityMode = context.VerbosityMode,
            ToneMode = context.ToneMode,
            ComprehensionExpressionVersion = context.ComprehensionExpressionVersion,
            ContextAwarenessMode = context.ContextAwarenessMode,
            ClarityDecision = context.ClarityDecision,
            ClarityReason = context.ClarityReason,
            ExpressionToneProfile = context.ExpressionToneProfile,
            ExpressionToneReason = context.ExpressionToneReason,
            TemplateUsed = context.TemplateUsed,
            PresentationFrameworkVersion = context.PresentationFrameworkVersion,
            PresentationTemplateFamily = context.PresentationTemplateFamily,
            PresentationTemplateReason = context.PresentationTemplateReason,
            PresentationRhythmMode = context.PresentationRhythmMode,
            PresentationRhythmReason = context.PresentationRhythmReason,
            PresentationFormattingMode = context.PresentationFormattingMode,
            PresentationFormattingReason = context.PresentationFormattingReason,
            PresentationSourceDisplayMode = context.PresentationSourceDisplayMode,
            PresentationSourceDisplayReason = context.PresentationSourceDisplayReason,
            ChatResponseQualityVersion = context.ChatResponseQualityVersion,
            ChatResponseProfile = context.ChatResponseProfile,
            ChatResponseProfileReason = context.ChatResponseProfileReason,
            ChatContextBudgetMode = context.ChatContextBudgetMode,
            ChatContextBudgetReason = context.ChatContextBudgetReason,
            ChatFinalPolishApplied = context.ChatFinalPolishApplied,
            McpTool = context.McpExecutionToolName,
            McpExecutionStatus = context.McpExecutionStatus,
            McpPolicyDecision = context.McpPolicyDecision,
            CommunicationRuntimeApplied = TrueOrNull(context.CommunicationRuntimeApplied),
            EvidenceRequired = TrueOrNull(context.EvidenceRequired),
            TruthEngineVersion = context.TruthEngineVersion,
            TruthEngineMode = context.TruthEngineMode,
            TruthState = context.TruthState,
            TruthReason = context.TruthReason,
            TruthEvidenceCount = context.TruthEvidenceCount,
            TruthConflictCount = context.TruthConflictCount,
            TruthEvidenceKinds = NullIfEmpty(context.TruthEvidenceKinds),
            TruthEvaluatedUtc = context.TruthEvaluatedUtc,
            Warnings = warnings,
            MemoryUpdated = TrueOrNull(context.MemoryUpdated),
            AppliedPreferenceMode = context.AppliedPreferenceMode,
            GovernedSourceCount = CountOrNull(context.BaselinePackGuidance.Count),
            CommunicationCoreApplied = TrueOrNull(context.CommunicationCoreApplied),
            CommunicationCoreVersion = communicationCore?.PackVersion,
            CommunicationCoreHash = communicationCore?.PackHash,
            FinalAnswerHash = context.FinalAnswerHash,
            Guardrails = context.GuardrailViolations,
            GuardrailsSummary = BuildGuardrailSummary(context),
            GuardrailsPointers = BuildGuardrailPointers(context),
            SurvivalMode = context.SurvivalMode,
            SurvivalProfile = context.SurvivalProfile,
            SurvivalLastSignalUtc = context.SurvivalLastSignalUtc,
            DiscrepancySeverity = context.DiscrepancySeverity,
            DiscrepancyReason = context.DiscrepancyReason,
            DiscrepancyScore = context.DiscrepancyScore,
            DiscrepancyRequiresApproval = TrueOrNull(context.DiscrepancyRequiresApproval),
            DiscrepancyTicketId = context.DiscrepancyTicketId,
            FallbackApplied = TrueOrNull(context.FallbackApplied),
            ReasoningReceipt = BuildReasoningReceipt(context),
            Discrepancies = context.Discrepancies,
            Corrections = context.Corrections,
            OperatorProfile = context.OperatorProfile,
            OperatorProfileDecision = context.OperatorProfileDecision,
            ExecutionState = NullIfBlank(context.ExecutionState),
            ExecutionTarget = NullIfBlank(context.ExecutionTarget),
            DeferredWorkId = context.DeferredWorkId,
            HomeNodeId = context.HomeNodeId,
            HomeNodeOwnerUserId = context.HomeNodeOwnerUserId,
            HomeNodeTailscaleIp = context.HomeNodeTailscaleIp,
            HomeNodeMembershipStatus = context.HomeNodeMembershipStatus,
            HomeNodeMembershipLastSeenUtc = context.HomeNodeMembershipLastSeenUtc,
            HomeNodeStatus = context.HomeNodeStatus,
            HomeNodeSourceCount = CountOrNull(context.HomeNodeSources.Count),
            HomeNodeExecutionMode = context.HomeNodeExecutionMode,
            HomeNodeLocalPolicyOutcome = context.HomeNodeLocalPolicyOutcome,
            HomeNodeBundleId = context.HomeNodeBundleId,
            HomeNodePackId = context.HomeNodePackId,
            HomeNodePackName = context.HomeNodePackName,
            HomeNodePackAnchor = context.HomeNodePackAnchor,
            HomeNodeSearchTerms = NullIfEmpty(context.HomeNodeSearchTerms)
        };
    }

    private static string DefaultIfBlank(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string? NullIfBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> values)
    {
        return values.Count == 0 ? null : values;
    }

    private static bool? TrueOrNull(bool value)
    {
        return value ? true : null;
    }

    private static T? ValueWhen<T>(bool condition, T? value)
        where T : class
    {
        return condition ? value : null;
    }

    private static int CountOrZero(bool condition, int count)
    {
        return condition ? count : 0;
    }

    private static int? CountWhen(bool condition, int count)
    {
        return condition ? count : null;
    }

    private static int? CountOrNull(int count)
    {
        return count == 0 ? null : count;
    }

    private static void EnforceFinalAnswerPolicy(AskWorkflowContext context)
    {
        if (context.EvidenceRequired && !AskEvidencePolicy.HasGroundedEvidence(context))
        {
            if (GovernedResponseModes.TryResolveStructuredMode(context, out var structuredTemplate, out var structuredTone))
            {
                context.TemplateUsed = structuredTemplate;
                context.ToneMode = structuredTone;
            }
            else
            {
                context.TemplateUsed = "low_confidence_escalation";
                context.ToneMode = "careful_escalation";
            }

            context.Answer = GovernedFallbackAnswerBuilder.BuildEvidenceUnavailable(context);
            context.FallbackApplied = true;
            context.RetrievalAllowed = false;
            context.RetrievalSkippedReason ??= "evidence_required_but_no_governed_evidence";
            context.Grounded = false;
            context.Retrieved.Clear();
            context.GraphEvidence.Clear();
            context.MemoryResults = Array.Empty<GovernedMemoryResult>();
            context.SourceResults = Array.Empty<GovernedSourceResult>();
        }

        context.Answer = GovernedAnswerPostProcessor.ApplyResponseOrchestration(context.Answer ?? string.Empty, context);
        ApplyTrustedWebCitationPolicy(context);
    }

    private static void ApplyTrustedWebCitationPolicy(AskWorkflowContext context)
    {
        if (!context.TrustedWebContextRequested || !context.TrustedWebContextEnabled)
        {
            return;
        }

        if (context.TrustedWebResults.Count == 0)
        {
            AppendTrustedWebUnavailableNote(context);
            return;
        }

        if (string.IsNullOrWhiteSpace(context.Answer))
        {
            return;
        }

        var refs = BuildTrustedWebCitationRefs(context.TrustedWebResults).ToArray();
        if (refs.Length == 0)
        {
            return;
        }

        var answer = RemoveUnsupportedCitationMarkers(context.Answer!, refs, context);
        if (HasSupportedTrustedWebMarker(answer, refs))
        {
            context.Answer = answer;
            return;
        }

        var consultedSources = string.Join(
            "; ",
            refs.Select(reference =>
            {
                var sourceTitle = string.IsNullOrWhiteSpace(reference.Title) ? reference.SourceId : reference.Title;
                return $"[{reference.Marker}] {sourceTitle}";
            }));
        context.Answer = string.Concat(
            answer.TrimEnd(),
            Environment.NewLine,
            Environment.NewLine,
            $"Trusted web sources consulted: {consultedSources}.").TrimEnd();
    }

    private static void AppendTrustedWebUnavailableNote(AskWorkflowContext context)
    {
        if (string.IsNullOrWhiteSpace(context.Answer)
            || context.Answer.Contains("Trusted web evidence was unavailable", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var warning = string.IsNullOrWhiteSpace(context.TrustedWebContextSkippedReason)
            ? "trusted_web_evidence_unavailable"
            : $"trusted_web_evidence_unavailable:{context.TrustedWebContextSkippedReason}";
        AddResponseWarning(context, warning);
        context.Answer = string.Concat(
            context.Answer.TrimEnd(),
            Environment.NewLine,
            Environment.NewLine,
            "Trusted web evidence was unavailable from the configured sites for this request, so no web citations are attached.").TrimEnd();
    }

    private static string RemoveUnsupportedCitationMarkers(
        string answer,
        IReadOnlyCollection<TrustedWebCitationRef> refs,
        AskWorkflowContext context)
    {
        var supportedMarkers = refs
            .Select(reference => reference.Marker)
            .ToHashSet();
        var removed = new SortedSet<int>();
        var cleaned = CitationMarkerRegex.Replace(answer, match =>
        {
            if (int.TryParse(match.Groups[1].Value, out var marker)
                && supportedMarkers.Contains(marker))
            {
                return match.Value;
            }

            if (int.TryParse(match.Groups[1].Value, out var unsupportedMarker))
            {
                removed.Add(unsupportedMarker);
            }

            return string.Empty;
        });

        foreach (var marker in removed)
        {
            AddResponseWarning(context, $"unsupported_trusted_web_citation_marker_removed:{marker}");
        }

        return cleaned;
    }

    private static bool HasSupportedTrustedWebMarker(
        string answer,
        IReadOnlyCollection<TrustedWebCitationRef> refs)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        var supportedMarkers = refs.Select(reference => reference.Marker).ToHashSet();
        foreach (Match match in CitationMarkerRegex.Matches(answer))
        {
            if (int.TryParse(match.Groups[1].Value, out var marker)
                && supportedMarkers.Contains(marker))
            {
                return true;
            }
        }

        return false;
    }

    private static void AddResponseWarning(AskWorkflowContext context, string warning)
    {
        if (!context.ResponseWarnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
        {
            context.ResponseWarnings.Add(warning);
        }
    }

    private static IReadOnlyList<string> BuildSources(AskWorkflowContext context)
    {
        var sources = new List<string>();

        if (ShouldReportRetrievedEvidence(context))
        {
            sources.AddRange(context.GraphEvidence.Select(evidence => evidence.SourceToken));
            sources.AddRange(context.MemoryResults.Select(memory => $"memory:{memory.MemoryId}"));
            sources.AddRange(context.SourceResults.Select(source => $"source:{source.SourceId}:{source.ChunkId}"));
            sources.AddRange(context.Retrieved.Select(result => $"{result.DocId}:{result.ChunkId}"));
            sources.AddRange(context.HomeNodeSources.Select(source => $"home_node:{ResolveHomeNodeSourceLabel(source)}"));
            sources.AddRange(context.BaselinePackGuidance.Select(pack => $"pack:{pack.PackName}@{pack.PackVersion}"));
        }
        else if (RequiresGovernedConfidenceFloor(context))
        {
            sources.AddRange(context.BaselinePackGuidance
                .Where(IsSystemTruthPack)
                .Select(pack => $"pack:{pack.PackName}@{pack.PackVersion}"));
        }

        if (!string.IsNullOrWhiteSpace(context.McpExecutionToolName))
        {
            sources.Add($"mcp_tool:{context.McpExecutionToolName}");
            sources.AddRange(BuildMcpSources(context));
        }

        sources.AddRange(context.TrustedWebResults.Select(TrustedWebEvidence.BuildEvidenceId));

        return sources
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> BuildMcpSources(AskWorkflowContext context)
    {
        if (context.McpResult is null
            || !context.McpResult.TryGetValue("sources", out var rawSources)
            || rawSources is null)
        {
            yield break;
        }

        foreach (var item in ExtractMcpSourceItems(rawSources))
        {
            var title = TryDictionaryString(item, "title") ?? TryDictionaryString(item, "source") ?? "MCP source";
            var url = TryDictionaryString(item, "url");
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            yield return string.IsNullOrWhiteSpace(url)
                ? $"mcp_source:{title}"
                : $"mcp_source:{title}|{url}";
        }
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> ExtractMcpSourceItems(object rawSources)
    {
        switch (rawSources)
        {
            case IEnumerable<IReadOnlyDictionary<string, object?>> dictionaries:
                foreach (var dictionary in dictionaries)
                {
                    yield return dictionary;
                }
                break;
            case System.Text.Json.JsonElement element when element.ValueKind == System.Text.Json.JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind != System.Text.Json.JsonValueKind.Object)
                    {
                        continue;
                    }

                    yield return item.EnumerateObject().ToDictionary(
                        property => property.Name,
                        property => (object?)JsonElementToString(property.Value),
                        StringComparer.OrdinalIgnoreCase);
                }
                break;
        }
    }

    private static string? JsonElementToString(System.Text.Json.JsonElement element)
    {
        return element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.String => element.GetString(),
            System.Text.Json.JsonValueKind.Number => element.ToString(),
            System.Text.Json.JsonValueKind.True => "true",
            System.Text.Json.JsonValueKind.False => "false",
            _ => null
        };
    }

    private static string? TryDictionaryString(IReadOnlyDictionary<string, object?> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value.ToString();
    }

    private static float CalculateConfidence(AskWorkflowContext context, bool grounded, out string confidenceBasis)
    {
        if (TryResolveMemoryUpdateConfidence(context, out var confidence, out confidenceBasis)
            || TryResolveDeterministicConfidence(context, out confidence, out confidenceBasis)
            || TryResolveClarificationConfidence(context, out confidence, out confidenceBasis)
            || TryResolveMcpConfidence(context, out confidence, out confidenceBasis)
            || TryResolveResponseModeConfidence(context, grounded, out confidence, out confidenceBasis)
            || TryResolveMemoryGraphConfidence(context, out confidence, out confidenceBasis)
            || TryResolveSystemTruthConfidence(context, out confidence, out confidenceBasis)
            || TryResolveUngroundedConfidence(context, grounded, out confidence, out confidenceBasis))
        {
            return confidence;
        }

        return CalculateGroundedEvidenceConfidence(context, out confidenceBasis);
    }

    private static bool TryResolveMemoryUpdateConfidence(
        AskWorkflowContext context,
        out float confidence,
        out string confidenceBasis)
    {
        if (HasAnyQueryClassification(context, "preference_update", "self_context_update") || context.MemoryUpdated)
        {
            confidence = 0.9f;
            confidenceBasis = "memory_update_confirmed";
            return true;
        }

        return NoConfidence(out confidence, out confidenceBasis);
    }

    private static bool TryResolveDeterministicConfidence(
        AskWorkflowContext context,
        out float confidence,
        out string confidenceBasis)
    {
        if (HasQueryClassification(context, "deterministic_reasoning"))
        {
            confidence = 0.95f;
            confidenceBasis = "deterministic_reasoning_non_grounded";
            return true;
        }

        if (HasQueryClassification(context, "deterministic_datetime"))
        {
            confidence = 0.99f;
            confidenceBasis = "deterministic_datetime_server_clock";
            return true;
        }

        return NoConfidence(out confidence, out confidenceBasis);
    }

    private static bool TryResolveClarificationConfidence(
        AskWorkflowContext context,
        out float confidence,
        out string confidenceBasis)
    {
        if (HasQueryClassification(context, "entity_lookup") && context.EntityFound == false)
        {
            confidence = 0.7f;
            confidenceBasis = "unknown_entity_clarification";
            return true;
        }

        if (HasQueryClassification(context, "clarification_required") || HasIntentType(context, "needs_clarification"))
        {
            confidence = 0.6f;
            confidenceBasis = "ambiguous_action_clarification";
            return true;
        }

        return NoConfidence(out confidence, out confidenceBasis);
    }

    private static bool TryResolveMcpConfidence(
        AskWorkflowContext context,
        out float confidence,
        out string confidenceBasis)
    {
        if (!HasQueryClassification(context, "tool_intent_request"))
        {
            return NoConfidence(out confidence, out confidenceBasis);
        }

        confidence = CalculateMcpConfidence(context, out confidenceBasis);
        return true;
    }

    private static bool TryResolveResponseModeConfidence(
        AskWorkflowContext context,
        bool grounded,
        out float confidence,
        out string confidenceBasis)
    {
        if (HasQueryClassification(context, "code_generation") || HasIntentType(context, "code_generation"))
        {
            confidence = 0.86f;
            confidenceBasis = "code_generation_template";
            return true;
        }

        if (HasQueryClassification(context, "summarization") || HasIntentType(context, "summarization"))
        {
            confidence = HasModelName(context, "non-grounded-summary-reasoner") ? 0.82f : 0.75f;
            confidenceBasis = "summarization_response_mode";
            return true;
        }

        if (HasQueryClassification(context, "proactive_intelligence_request"))
        {
            confidence = grounded ? 0.85f : 0.65f;
            confidenceBasis = grounded
                ? "proactive_intelligence_api"
                : "proactive_intelligence_api_no_current_items";
            return true;
        }

        return NoConfidence(out confidence, out confidenceBasis);
    }

    private static bool TryResolveMemoryGraphConfidence(
        AskWorkflowContext context,
        out float confidence,
        out string confidenceBasis)
    {
        if (!HasQueryClassification(context, "memory_graph_request") && !HasIntentType(context, "memory_recall"))
        {
            return NoConfidence(out confidence, out confidenceBasis);
        }

        if (context.MemoryResults.Count > 0)
        {
            confidence = Math.Max(0.82f, Math.Min(0.94f, context.MemoryResults.Average(memory => memory.Confidence)));
            confidenceBasis = "activated_user_memory";
            return true;
        }

        if (context.GraphEvidence.Count > 0)
        {
            confidence = 0.80f;
            confidenceBasis = "user_memory_graph";
            return true;
        }

        confidence = 0.35f;
        confidenceBasis = "user_memory_unavailable";
        return true;
    }

    private static bool TryResolveSystemTruthConfidence(
        AskWorkflowContext context,
        out float confidence,
        out string confidenceBasis)
    {
        if (HasQueryClassification(context, "identity_definition") && HasModelName(context, "canonical-system-truth"))
        {
            confidence = 0.95f;
            confidenceBasis = "canonical_system_truth";
            return true;
        }

        if (RequiresGovernedConfidenceFloor(context)
            && TryResolveGovernedConfidenceFloor(context, out var governedFloor))
        {
            confidence = Math.Max(0.95f, governedFloor);
            confidenceBasis = "governed_system_truth";
            return true;
        }

        return NoConfidence(out confidence, out confidenceBasis);
    }

    private static bool TryResolveUngroundedConfidence(
        AskWorkflowContext context,
        bool grounded,
        out float confidence,
        out string confidenceBasis)
    {
        if (grounded)
        {
            return NoConfidence(out confidence, out confidenceBasis);
        }

        if (context.EvidenceRequired || HasTemplateUsed(context, "low_confidence_escalation"))
        {
            confidence = 0.2f;
            confidenceBasis = "evidence_required_without_grounded_evidence";
            return true;
        }

        if (context.FallbackApplied)
        {
            confidence = 0.45f;
            confidenceBasis = "fallback_response_without_grounded_evidence";
            return true;
        }

        confidence = 0.8f;
        confidenceBasis = HasGroundingMode(context, "non_grounded")
            ? "non_grounded_general_reasoning"
            : "ungrounded_model_reasoning";
        return true;
    }

    private static float CalculateGroundedEvidenceConfidence(AskWorkflowContext context, out string confidenceBasis)
    {
        var confidence = ResolveEvidenceConfidence(context);
        confidenceBasis = context.MemoryResults.Any(IsGroundingMemory) || context.SourceResults.Count > 0
            ? "governed_memory_or_source_evidence"
            : "source_evidence";

        if (context.GraphUnavailable && context.GraphEvidence.Count == 0)
        {
            confidence = Math.Max(0.1f, confidence - 0.1f);
            confidenceBasis = "source_evidence_graph_unavailable";
        }

        return confidence;
    }

    private static bool NoConfidence(out float confidence, out string confidenceBasis)
    {
        confidence = 0.0f;
        confidenceBasis = string.Empty;
        return false;
    }

    private static bool HasAnyQueryClassification(AskWorkflowContext context, params string[] classifications)
    {
        return classifications.Any(classification => HasQueryClassification(context, classification));
    }

    private static bool HasQueryClassification(AskWorkflowContext context, string classification)
    {
        return string.Equals(context.QueryClassification, classification, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasIntentType(AskWorkflowContext context, string intentType)
    {
        return string.Equals(context.IntentType, intentType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasModelName(AskWorkflowContext context, string modelName)
    {
        return string.Equals(context.ModelName, modelName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasTemplateUsed(AskWorkflowContext context, string template)
    {
        return string.Equals(context.TemplateUsed, template, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasGroundingMode(AskWorkflowContext context, string groundingMode)
    {
        return string.Equals(context.GroundingMode, groundingMode, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveConfidenceLevel(AskWorkflowContext context, bool grounded, float confidence)
    {
        if ((context.EvidenceRequired
                || string.Equals(context.TemplateUsed, "low_confidence_escalation", StringComparison.OrdinalIgnoreCase))
            && !grounded)
        {
            return "blocked";
        }

        if (confidence >= 0.90f)
        {
            return "high";
        }

        if (confidence >= 0.70f)
        {
            return "medium";
        }

        return "low";
    }

    private static float CalculateMcpConfidence(AskWorkflowContext context, out string confidenceBasis)
    {
        var status = context.McpExecutionStatus ?? string.Empty;
        if (string.Equals(context.McpPolicyDecision, "blocked", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "blocked", StringComparison.OrdinalIgnoreCase))
        {
            confidenceBasis = "mcp_policy_blocked";
            return 0.78f;
        }

        if (string.Equals(context.McpPolicyDecision, "approval_required", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "approval_required", StringComparison.OrdinalIgnoreCase))
        {
            confidenceBasis = "mcp_policy_approval_required";
            return 0.76f;
        }

        if (!context.McpExecuted)
        {
            confidenceBasis = string.IsNullOrWhiteSpace(status)
                ? "mcp_tool_intent_gated"
                : "mcp_tool_adapter_status";
            return status.Contains("failed", StringComparison.OrdinalIgnoreCase)
                || status.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                    ? 0.45f
                    : 0.62f;
        }

        if (string.Equals(context.McpExecutionToolName, "search.web", StringComparison.OrdinalIgnoreCase))
        {
            var sourceCount = CountMcpSources(context);
            confidenceBasis = sourceCount > 0
                ? "mcp_search_results_with_sources"
                : "mcp_search_executed_no_displayable_sources";
            return sourceCount > 0 ? 0.88f : 0.72f;
        }

        if (string.Equals(context.McpExecutionToolName, "weather.get", StringComparison.OrdinalIgnoreCase))
        {
            confidenceBasis = "mcp_weather_provider_forecast";
            return 0.88f;
        }

        confidenceBasis = "mcp_tool_execution";
        return 0.84f;
    }

    private static int CountMcpSources(AskWorkflowContext context)
    {
        if (context.McpResult is null
            || !context.McpResult.TryGetValue("sources", out var rawSources)
            || rawSources is null)
        {
            return 0;
        }

        return ExtractMcpSourceItems(rawSources).Count();
    }

    private static float ResolveEvidenceConfidence(AskWorkflowContext context)
    {
        var hasDomainEvidence = context.GraphEvidence.Count > 0
                                || context.Retrieved.Count > 0
                                || context.HomeNodeSources.Count > 0;
        var hasSources = context.SourceResults.Count > 0;
        var hasMemory = context.MemoryResults.Any(IsGroundingMemory);

        if (hasDomainEvidence || hasSources)
        {
            return hasMemory ? 0.87f : 0.85f;
        }

        if (hasMemory)
        {
            return 0.70f;
        }

        return 0.60f;
    }

    private static bool IsSystemTruthQuery(AskWorkflowContext context)
    {
        return context.QueryClassification is "identity_definition" or "home_node_definition" or "system_truth";
    }

    private static bool ResolveGrounded(AskWorkflowContext context)
    {
        if (context.EvidenceRequired)
        {
            return AskEvidencePolicy.HasGroundedEvidence(context);
        }

        if (context.Grounded.HasValue)
        {
            return context.Grounded.Value;
        }

        return context.MemoryUpdated
               || string.Equals(context.GroundingMode, "memory_update", StringComparison.OrdinalIgnoreCase)
               || HasDomainGrounding(context)
               || HasSystemTruthGrounding(context);
    }

    private static bool ResolveReasoningBased(AskWorkflowContext context, bool grounded)
    {
        if (context.ReasoningBased.HasValue)
        {
            return context.ReasoningBased.Value;
        }

        if (grounded || string.IsNullOrWhiteSpace(context.Answer))
        {
            return false;
        }

        if (string.Equals(context.HomeNodeExecutionMode, "local_model_ollama", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(context.ExecutionTarget, "shared_compute", StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(context.ModelName);
    }

    private static bool HasDomainGrounding(AskWorkflowContext context)
    {
        if (!AskEvidencePolicy.ShouldUseRetrievedEvidence(context))
        {
            return false;
        }

        var hasSourceEvidence = context.GraphEvidence.Count > 0
            || context.Retrieved.Count > 0
            || context.SourceResults.Count > 0
            || context.HomeNodeSources.Count > 0;
        if (context.EvidenceRequired)
        {
            return hasSourceEvidence;
        }

        return hasSourceEvidence || context.MemoryResults.Any(IsGroundingMemory);
    }

    private static bool HasSystemTruthGrounding(AskWorkflowContext context)
    {
        return IsSystemTruthQuery(context)
               && (string.Equals(context.ModelName, "canonical-system-truth", StringComparison.OrdinalIgnoreCase)
                   || context.BaselinePackGuidance.Any(pack =>
                       pack.HasRole("system_truth")
                       || string.Equals(pack.PackName, "cwi-identity", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsGroundingMemory(GovernedMemoryResult memory)
    {
        return memory.RelevanceScore >= 0.80f && memory.Confidence >= 0.70f;
    }

    private static IReadOnlyList<CitationDto> BuildCitations(AskWorkflowContext context)
    {
        var citations = ShouldReportRetrievedEvidence(context)
            ? context.Retrieved.Select(r => new CitationDto
            {
                DocId = r.DocId,
                ChunkId = r.ChunkId,
                Score = r.Score
            }).ToList()
            : new List<CitationDto>();

        citations.AddRange(context.TrustedWebResults.Select((result, index) => new CitationDto
        {
            DocId = $"web:{result.SourceId}",
            ChunkId = result.ChunkId,
            Score = result.Score,
            Marker = index + 1,
            SourceId = TrustedWebEvidence.BuildEvidenceId(result),
            SourceType = result.SourceType,
            Title = ResolveTrustedWebTitle(result),
            Url = result.Url,
            Excerpt = result.Snippet,
            ContentSha256 = result.ContentHash,
            RetrievedUtc = result.RetrievedUtc
        }));

        if (!RequiresGovernedConfidenceFloor(context))
        {
            return citations;
        }

        foreach (var pack in context.BaselinePackGuidance
                     .Where(IsSystemTruthPack)
                     .DistinctBy(pack => pack.PackName, StringComparer.OrdinalIgnoreCase))
        {
            citations.Add(new CitationDto
            {
                DocId = pack.PackName,
                ChunkId = $"governed:{pack.PackVersion}"
            });
        }

        return citations;
    }

    private static ReasoningReceipt BuildReasoningReceipt(AskWorkflowContext context)
    {
        var useRetrievedEvidence = ShouldReportRetrievedEvidence(context);
        return new ReasoningReceipt
        {
            Grounded = context.Grounded ?? AskEvidencePolicy.HasGroundedEvidence(context),
            GroundingMode = context.GroundingMode,
            RetrievalAllowed = context.RetrievalAllowed,
            RetrievalSkippedReason = context.RetrievalSkippedReason,
            ConfidenceBasis = context.ConfidenceBasis,
            GraphUsed = useRetrievedEvidence && context.GraphEvidence.Count > 0,
            GraphQueryTemplate = useRetrievedEvidence ? context.GraphQueryTemplate : null,
            GraphTerms = useRetrievedEvidence ? context.GraphQueryTerms : Array.Empty<string>(),
            GraphFallbackUsed = useRetrievedEvidence && context.GraphUnavailable && context.GraphEvidence.Count == 0,
            GraphFailureReason = context.GraphFailureReason,
            GraphEvidenceCount = useRetrievedEvidence ? context.GraphEvidence.Count : 0,
            DocumentEvidenceCount = useRetrievedEvidence ? context.Retrieved.Count : 0
        };
    }

    private static IReadOnlyList<MemoryTraceItem> BuildMemoryTrace(AskWorkflowContext context)
    {
        return context.MemoryResults.Select(memory => new MemoryTraceItem
        {
            MemoryId = memory.MemoryId,
            MemoryScope = memory.MemoryScope,
            SourceType = memory.SourceType,
            TenantId = memory.TenantId,
            UserId = memory.UserId,
            NodeId = memory.NodeId,
            RelevanceScore = memory.RelevanceScore,
            Confidence = memory.Confidence,
            ContentSha256 = memory.ContentSha256,
            ProvenanceId = memory.ProvenanceId
        }).ToArray();
    }

    private static IReadOnlyList<MemoryFrameworkTraceDecision>? BuildMemoryFrameworkDecisions(AskWorkflowContext context)
    {
        if (context.MemoryFrameworkDecisions.Count == 0)
        {
            return null;
        }

        return context.MemoryFrameworkDecisions
            .Take(20)
            .Select(decision => new MemoryFrameworkTraceDecision
            {
                MemoryId = decision.MemoryId,
                Slot = decision.Slot,
                Outcome = decision.Outcome,
                Reason = decision.Reason,
                TemporalState = decision.TemporalState
            })
            .ToArray();
    }

    private static IReadOnlyList<MemoryConflictTraceItem>? BuildMemoryConflictTrace(AskWorkflowContext context)
    {
        if (context.MemoryConflictSummaries.Count == 0)
        {
            return null;
        }

        return context.MemoryConflictSummaries
            .Take(10)
            .Select(conflict => new MemoryConflictTraceItem
            {
                ConflictId = conflict.ConflictId,
                Slot = conflict.Slot,
                MemoryIds = conflict.MemoryIds
            })
            .ToArray();
    }

    private static IReadOnlyList<SourceTraceItem> BuildSourceTrace(AskWorkflowContext context)
    {
        return context.SourceResults.Select(source => new SourceTraceItem
        {
            SourceId = source.SourceId,
            ChunkId = source.ChunkId,
            SourceType = source.SourceType,
            TenantId = source.TenantId,
            UserId = source.UserId,
            NodeId = source.NodeId,
            RelevanceScore = source.RelevanceScore,
            ContentSha256 = source.ContentSha256,
            ExtractionMethod = source.ExtractionMethod
        }).ToArray();
    }

    private static IReadOnlyList<TrustedWebTraceSource>? BuildTrustedWebTrace(AskWorkflowContext context)
    {
        if (!context.TrustedWebContextRequested)
        {
            return null;
        }

        return context.TrustedWebResults.Select((source, index) => new TrustedWebTraceSource
        {
            SourceId = source.SourceId,
            EvidenceSourceId = TrustedWebEvidence.BuildEvidenceId(source),
            Marker = index + 1,
            SourceType = source.SourceType,
            TrustTier = source.TrustTier,
            Publisher = source.SourceName,
            Title = ResolveTrustedWebTitle(source),
            Url = source.Url,
            CanonicalUrl = string.IsNullOrWhiteSpace(source.CanonicalUrl) ? source.Url : source.CanonicalUrl,
            ChunkId = source.ChunkId,
            Excerpt = source.Snippet,
            ContentSha256 = source.ContentHash,
            Score = source.Score,
            RetrievedUtc = source.RetrievedUtc
        }).ToArray();
    }

    private static IReadOnlyList<TrustedWebCitationRef> BuildTrustedWebCitationRefs(
        IReadOnlyList<TrustedWebResult> results)
    {
        return results
            .Select((result, index) => new TrustedWebCitationRef(
                Marker: index + 1,
                SourceId: TrustedWebEvidence.BuildEvidenceId(result),
                Title: ResolveTrustedWebTitle(result),
                Url: result.Url))
            .ToArray();
    }

    private static string ResolveTrustedWebTitle(TrustedWebResult source)
    {
        if (!string.IsNullOrWhiteSpace(source.Title))
        {
            return source.Title;
        }

        if (!string.IsNullOrWhiteSpace(source.SourceName))
        {
            return source.SourceName;
        }

        return source.SourceId;
    }

    private static IReadOnlyList<string> BuildUnsupportedClaims(AskWorkflowContext context)
    {
        var claims = new List<string>();
        if (context.TrustedWebContextRequested
            && context.TrustedWebContextEnabled
            && context.TrustedWebResults.Count == 0)
        {
            claims.Add("trusted_web_evidence_unavailable");
        }

        claims.AddRange(context.ResponseWarnings
            .Where(warning => warning.StartsWith("unsupported_trusted_web_citation_marker_removed:", StringComparison.OrdinalIgnoreCase)));

        return claims
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ResolveRetrievalStatus(AskWorkflowContext context)
    {
        if (context.TrustedWebContextRequested)
        {
            if (!context.TrustedWebContextEnabled)
            {
                return "disabled";
            }

            if (context.TrustedWebResults.Count > 0)
            {
                return "success";
            }

            if (context.TrustedWebContextUnavailable || !string.IsNullOrWhiteSpace(context.TrustedWebContextSkippedReason))
            {
                return "degraded";
            }

            return "none";
        }

        if (context.SourceRetrievalUnavailable || context.RetrievalUnavailable)
        {
            return "degraded";
        }

        if (!string.IsNullOrWhiteSpace(context.RetrievalSkippedReason) || context.RetrievalAllowed == false)
        {
            return "skipped";
        }

        if (context.GraphEvidence.Count > 0
            || context.Retrieved.Count > 0
            || context.SourceResults.Count > 0
            || context.HomeNodeSources.Count > 0)
        {
            return "success";
        }

        return null;
    }

    private static IReadOnlyList<PackCapabilitySummary> BuildPackCapabilitySummary(AskWorkflowContext context)
    {
        var capabilities = context.PackCapabilities?.Capabilities;
        if (capabilities is null || capabilities.Count == 0)
        {
            return Array.Empty<PackCapabilitySummary>();
        }

        return capabilities.Select(capability => new PackCapabilitySummary
        {
            Name = capability.Name,
            Scope = capability.Scope,
            Risk = capability.Risk?.ToString().ToLowerInvariant(),
            RequiresApproval = capability.RequiresApproval
        }).ToList();
    }

    private static PackCapabilityDecisionSummary? BuildPackCapabilityDecision(AskWorkflowContext context)
    {
        var decision = context.PackCapabilityDecision;
        if (decision is null)
        {
            return null;
        }

        return new PackCapabilityDecisionSummary
        {
            PolicyMode = decision.PolicyMode,
            Risk = decision.Risk.ToString().ToLowerInvariant(),
            RequiresApproval = decision.RequiresApproval,
            AllowExecution = decision.AllowExecution,
            BlockingCapabilities = decision.BlockingCapabilities,
            Reason = decision.Reason
        };
    }

    private static IReadOnlyList<BaselinePackTraceItem> BuildBaselinePackSummary(AskWorkflowContext context)
    {
        if (context.BaselinePackDocuments.Count == 0)
        {
            return Array.Empty<BaselinePackTraceItem>();
        }

        return context.BaselinePackDocuments.Select(pack => new BaselinePackTraceItem
        {
            PackId = pack.Name,
            PackVersion = pack.Version,
            PackHash = pack.Hash
        }).ToList();
    }

    private static IReadOnlyList<string> BuildPacksUsed(AskWorkflowContext context)
    {
        if (SuppressPackUsage(context))
        {
            return Array.Empty<string>();
        }

        return context.BaselinePackGuidance
            .Select(pack => pack.PackName)
            .Concat(context.DomainPackNames)
            .Where(packName => !string.IsNullOrWhiteSpace(packName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> BuildPackHashes(AskWorkflowContext context)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(context.PackName) && !string.IsNullOrWhiteSpace(context.PackHash))
        {
            hashes[context.PackName] = context.PackHash;
        }

        foreach (var pack in context.BaselinePackGuidance)
        {
            if (!string.IsNullOrWhiteSpace(pack.PackName) && !string.IsNullOrWhiteSpace(pack.PackHash))
            {
                hashes[pack.PackName] = pack.PackHash;
            }
        }

        foreach (var pack in context.BaselinePackDocuments)
        {
            if (!string.IsNullOrWhiteSpace(pack.Name) && !string.IsNullOrWhiteSpace(pack.Hash))
            {
                hashes[pack.Name] = pack.Hash;
            }
        }

        return hashes;
    }

    private static bool RequiresGovernedConfidenceFloor(AskWorkflowContext context)
    {
        return IsSystemTruthQuery(context)
               && context.BaselinePackGuidance.Any(pack => string.Equals(pack.PackName, "cwi-identity", StringComparison.OrdinalIgnoreCase));
    }

    private sealed record TrustedWebCitationRef(int Marker, string SourceId, string Title, string Url);

    private static string ResolveRoute(AskWorkflowContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.Route))
        {
            return context.Route!;
        }

        if (context.McpExecuted)
        {
            return "mcp_tool_execution";
        }

        if (!string.IsNullOrWhiteSpace(context.McpExecutionStatus))
        {
            return "mcp_tool_status";
        }

        if (string.Equals(context.QueryClassification, "general_model_knowledge", StringComparison.OrdinalIgnoreCase))
        {
            return "general_model_knowledge";
        }

        if (string.Equals(context.QueryClassification, "current_events", StringComparison.OrdinalIgnoreCase))
        {
            return "current_events_model_knowledge";
        }

        if (string.Equals(context.QueryClassification, "proactive_intelligence_request", StringComparison.OrdinalIgnoreCase))
        {
            return "proactive_intelligence";
        }

        if (context.GraphEvidence.Count > 0 || context.MemoryResults.Count > 0 || context.SourceResults.Count > 0)
        {
            return "evidence_augmented_answer";
        }

        if (HasPackUsage(context))
        {
            return "pack_model_answer";
        }

        return context.SkipModelInference ? "direct_answer" : "model_answer";
    }

    private static bool HasPackUsage(AskWorkflowContext context)
    {
        if (SuppressPackUsage(context))
        {
            return false;
        }

        return context.PackDocument is not null
               || context.BaselinePackGuidance.Count > 0
               || context.BaselinePackDocuments.Count > 0;
    }

    private static bool SuppressPackUsage(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "general_model_knowledge", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.QueryClassification, "current_events", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.QueryClassification, "proactive_intelligence_request", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.Route, "general_model_knowledge", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.Route, "current_events_model_knowledge", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.Route, "proactive_intelligence", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasMemoryUsage(AskWorkflowContext context)
    {
        return context.MemoryResults.Count > 0 || context.SourceResults.Count > 0;
    }

    private static bool ShouldReportRetrievedEvidence(AskWorkflowContext context)
    {
        return AskEvidencePolicy.ShouldUseRetrievedEvidence(context)
               && (!context.EvidenceRequired || AskEvidencePolicy.HasGroundedEvidence(context));
    }

    private static bool ShouldReportNormalizationTrace(AskWorkflowContext context)
    {
        return context.NormalizationApplied
               || context.NormalizationFailed
               || context.NormalizationRequiresVerification
               || context.NormalizationConfidence < context.NormalizationConfidenceThreshold
               || context.NormalizationRules.Count > 0;
    }

    private static string? ResolveTraceRawPrompt(AskWorkflowContext context)
    {
        return ShouldReportNormalizationTrace(context) ? context.RawQuestion : null;
    }

    private static string? ResolveTraceNormalizedPrompt(AskWorkflowContext context)
    {
        return ShouldReportNormalizationTrace(context) ? context.RoutingQuestion : null;
    }

    private static bool? ResolveNormalizationRequiresVerification(AskWorkflowContext context)
    {
        return context.NormalizationRequiresVerification ? true : null;
    }

    private static float? ResolveNormalizationConfidence(AskWorkflowContext context)
    {
        return ShouldReportNormalizationTrace(context) ? context.NormalizationConfidence : null;
    }

    private static float? ResolveNormalizationConfidenceThreshold(AskWorkflowContext context)
    {
        return ShouldReportNormalizationTrace(context) ? context.NormalizationConfidenceThreshold : null;
    }

    private static string? ResolveNormalizationVerificationMessage(AskWorkflowContext context)
    {
        return context.NormalizationRequiresVerification ? context.NormalizationVerificationMessage : null;
    }

    private static IReadOnlyList<string>? ResolveNormalizationVerificationCandidates(AskWorkflowContext context)
    {
        if (!context.NormalizationRequiresVerification || context.NormalizationVerificationCandidates.Count == 0)
        {
            return null;
        }

        return context.NormalizationVerificationCandidates;
    }

    private static string? ResolveNormalizationCorrectionDecision(AskWorkflowContext context)
    {
        return ShouldReportNormalizationTrace(context)
            && !string.Equals(context.NormalizationCorrectionDecision, "none", StringComparison.OrdinalIgnoreCase)
            ? context.NormalizationCorrectionDecision
            : null;
    }

    private static string? ResolveNormalizationCorrectionRisk(AskWorkflowContext context)
    {
        return ShouldReportNormalizationTrace(context)
            && !string.Equals(context.NormalizationCorrectionRisk, "none", StringComparison.OrdinalIgnoreCase)
            ? context.NormalizationCorrectionRisk
            : null;
    }

    private static string? ResolveNormalizationCorrectionVisibility(AskWorkflowContext context)
    {
        return ShouldReportNormalizationTrace(context)
            && !string.Equals(context.NormalizationCorrectionVisibility, "none", StringComparison.OrdinalIgnoreCase)
            ? context.NormalizationCorrectionVisibility
            : null;
    }

    private static IReadOnlyList<NormalizationCorrectionTrace>? BuildNormalizationCorrectionTrace(AskWorkflowContext context)
    {
        if (!ShouldReportNormalizationTrace(context) || context.NormalizationCorrections.Count == 0)
        {
            return null;
        }

        return context.NormalizationCorrections
            .Select(correction => new NormalizationCorrectionTrace
            {
                Original = correction.Original,
                Normalized = correction.Normalized,
                Category = correction.Category,
                Visibility = correction.Visibility,
                Reason = correction.Reason
            })
            .ToArray();
    }

    private static bool IsSystemTruthPack(BaselinePackGuidance pack)
    {
        return pack.HasRole("system_truth")
               || string.Equals(pack.PackName, "cwi-identity", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveGovernedConfidenceFloor(AskWorkflowContext context, out float floor)
    {
        floor = 0.95f;
        var identityPack = context.BaselinePackGuidance.FirstOrDefault(pack =>
            string.Equals(pack.PackName, "cwi-identity", StringComparison.OrdinalIgnoreCase));
        if (identityPack is null)
        {
            return false;
        }

        if (identityPack.ConfidenceFloor.HasValue)
        {
            floor = Math.Max(0.0f, Math.Min(1.0f, identityPack.ConfidenceFloor.Value));
        }

        return true;
    }

    private static BaselinePackGuidance? ResolveCommunicationCore(AskWorkflowContext context)
    {
        return context.BaselinePackGuidance.FirstOrDefault(pack =>
            pack.AppliesAtStage("final_response_composition")
            || pack.HasRole("global_communication_layer")
            || string.Equals(pack.PackName, "cwi-communication-core", StringComparison.OrdinalIgnoreCase));
    }

    private static string? BuildFinalAnswerHash(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(answer.Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static GuardrailSummary? BuildGuardrailSummary(AskWorkflowContext context)
    {
        if (context.GuardrailDiagnostics is null)
        {
            if (context.GuardrailViolations.Count == 0)
            {
                return null;
            }

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var violation in context.GuardrailViolations)
            {
                var key = violation.Severity?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
            }

            return new GuardrailSummary
            {
                Total = context.GuardrailViolations.Count,
                SeverityCounts = counts
            };
        }

        return new GuardrailSummary
        {
            Total = context.GuardrailDiagnostics.TotalViolations,
            SeverityCounts = context.GuardrailDiagnostics.SeverityCounts
        };
    }

    private static IReadOnlyList<string> BuildGuardrailPointers(AskWorkflowContext context)
    {
        if (context.GuardrailDiagnostics is not null)
        {
            return context.GuardrailDiagnostics.ValidatorCounts.Keys.ToList();
        }

        if (context.GuardrailViolations.Count == 0)
        {
            return Array.Empty<string>();
        }

        return context.GuardrailViolations
            .Select(v => v.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveHomeNodeSourceLabel(HomeNodeSourceDto source)
    {
        if (!string.IsNullOrWhiteSpace(source.Title))
        {
            return source.Title;
        }

        if (!string.IsNullOrWhiteSpace(source.Path))
        {
            return source.Path;
        }

        return source.SourceKind;
    }
}
