#nullable enable
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Documents;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Model;
using Cwi.Orchestrator.Application.OperatorMemory;
using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Application.Packs.Policy;
using Cwi.Orchestrator.Application.Policy;
using Cwi.Orchestrator.Application.Retrieval;
using Cwi.Orchestrator.Application.Survival;
using Cwi.Orchestrator.Application.Telemetry;
using Cwi.Orchestrator.Application.TrustedWeb;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskPackResolveStep : IAskWorkflowStep
{
    private readonly IPackResolver _packResolver;
    private readonly ILogger<AskPackResolveStep> _logger;
    private readonly JourneyEmitter _journeyEmitter;

    public AskPackResolveStep(
        IPackResolver packResolver,
        ILogger<AskPackResolveStep> logger,
        JourneyEmitter journeyEmitter)
    {
        _packResolver = packResolver;
        _logger = logger;
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        await _journeyEmitter.EmitAsync(
            "pack_resolver",
            "started",
            fromComponent: "cwi-orchestrator",
            toComponent: "pack_resolver",
            metadata: new Dictionary<string, object?> { ["pack_name"] = context.PackName },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var packResult = await _packResolver.ResolveAsync(context.PackName, cancellationToken).ConfigureAwait(false);
        await _journeyEmitter.EmitAsync(
            "pack_resolver",
            packResult.IsSuccess && packResult.Data is not null ? "completed" : "failed",
            direction: "response",
            fromComponent: "pack_resolver",
            toComponent: "cwi-orchestrator",
            statusCode: packResult.StatusCode,
            durationMs: (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            message: packResult.Message,
            metadata: new Dictionary<string, object?>
            {
                ["contract_type"] = packResult.Data?.Root.ContractType.ToString(),
                ["pack_hash"] = packResult.Data?.Root.Hash,
                ["pack_version"] = packResult.Data?.Root.Version,
                ["baseline_packs"] = packResult.Data?.BaselinePacks.Select(pack => pack.Name).ToArray() ?? Array.Empty<string>()
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!packResult.IsSuccess || packResult.Data is null)
        {
            return ServiceResponse<AskResponseDto>.Fail(
                packResult.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "Pack resolve failed."),
                statusCode: packResult.StatusCode,
                message: packResult.Message);
        }

        context.PackDocument = packResult.Data.Root;
        context.PackMetadata = PackManifestReader.Read(packResult.Data.Root);
        context.PackHash = packResult.Data.Root.Hash;
        context.PackVersion = packResult.Data.Root.Version;
        context.PackCapabilities = packResult.Data.CapabilityMap;
        context.BaselinePackDocuments = packResult.Data.BaselinePacks;
        context.BaselinePackMetadata = packResult.Data.BaselinePacks
            .ToDictionary(pack => pack.Name, pack => PackManifestReader.Read(pack), StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(context.RequestedPackAnchor) &&
            !string.IsNullOrWhiteSpace(context.PackHash) &&
            !string.Equals(context.RequestedPackAnchor.Trim(), context.PackHash, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Requested pack anchor mismatch for {PackName}. Requested {Requested} vs resolved {Resolved}.",
                context.PackName,
                context.RequestedPackAnchor,
                context.PackHash);
            return ServiceResponse<AskResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Pack anchor mismatch."),
                statusCode: 400,
                message: "Pack verification failed");
        }

        return await next().ConfigureAwait(false);
    }
}

public sealed class AskBaselinePackGuidanceStep : IAskWorkflowStep
{
    private readonly BaselinePackContentProvider _baselinePackContentProvider;
    private readonly JourneyEmitter _journeyEmitter;

    public AskBaselinePackGuidanceStep(
        BaselinePackContentProvider baselinePackContentProvider,
        JourneyEmitter journeyEmitter)
    {
        _baselinePackContentProvider = baselinePackContentProvider;
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (context.BaselinePackDocuments.Count == 0)
        {
            return await next().ConfigureAwait(false);
        }

        var started = DateTimeOffset.UtcNow;
        await _journeyEmitter.EmitAsync(
            "core_pack_loader",
            "started",
            fromComponent: "cwi-orchestrator",
            toComponent: "core_pack_runtime",
            metadata: new Dictionary<string, object?>
            {
                ["core_pack_order"] = CorePackOrdering.CorePackOrder,
                ["final_layer_order"] = CorePackOrdering.FinalLayerOrder,
                ["baseline_packs"] = context.BaselinePackDocuments.Select(pack => pack.Name).ToArray()
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var guidance = await _baselinePackContentProvider
            .FetchAsync(context.BaselinePackDocuments, cancellationToken)
            .ConfigureAwait(false);

        await _journeyEmitter.EmitAsync(
            "core_pack_loader",
            guidance.IsSuccess && guidance.Data is not null ? "completed" : "failed",
            direction: "response",
            fromComponent: "core_pack_runtime",
            toComponent: "cwi-orchestrator",
            statusCode: guidance.StatusCode,
            durationMs: (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            message: guidance.Message,
            metadata: new Dictionary<string, object?>
            {
                ["loaded_pack_count"] = guidance.Data?.Count ?? 0
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!guidance.IsSuccess || guidance.Data is null)
        {
            return ServiceResponse<AskResponseDto>.Fail(
                guidance.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "Baseline pack content unavailable."),
                statusCode: guidance.StatusCode,
                message: guidance.Message);
        }

        context.BaselinePackGuidance = guidance.Data;
        var guidancePackNames = guidance.Data.Select(pack => pack.PackName).ToArray();
        context.CorePackExecutionOrder = CorePackOrdering.PresentCorePacks(guidancePackNames);
        context.FinalLayerExecutionOrder = CorePackOrdering.PresentFinalLayers(guidancePackNames);

        return await next().ConfigureAwait(false);
    }
}

public sealed class AskQueryClassificationStep : IAskWorkflowStep
{
    private readonly AskQueryClassifier _classifier;
    private readonly JourneyEmitter _journeyEmitter;

    public AskQueryClassificationStep(AskQueryClassifier classifier, JourneyEmitter journeyEmitter)
    {
        _classifier = classifier;
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (context.NormalizationFailed)
        {
            return await ApplyClarificationFallbackAsync(
                context,
                next,
                "Language normalization failed; clarification route selected.",
                cancellationToken).ConfigureAwait(false);
        }

        AskQueryClassification classification;
        try
        {
            classification = _classifier.Classify(
                context.RoutingQuestion,
                context.BaselinePackMetadata.Values.ToArray());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return await ApplyClarificationFallbackAsync(
                context,
                next,
                "Classification failed; clarification route selected.",
                cancellationToken).ConfigureAwait(false);
        }

        context.QueryClassification = classification.Classification;
        context.QueryClassificationReason = classification.Reason;
        context.IntentType = classification.IntentType;
        context.AudienceMode = classification.AudienceMode;
        context.VerbosityMode = classification.VerbosityMode;
        context.RetrievalAllowed = classification.RetrievalAllowed;
        context.GroundingMode = classification.GroundingMode;
        context.RetrievalDomain = classification.RetrievalDomain;
        context.RetrievalDomainTerms = classification.RetrievalDomainTerms;
        context.RetrievalSkippedReason = classification.RetrievalSkippedReason;
        context.RetrievalMinScore = classification.RetrievalMinScore;
        context.EvidenceRequired = classification.EvidenceRequired;
        context.MemoryUpdateKind = classification.MemoryUpdateKind;
        context.MemoryUpdateValue = classification.MemoryUpdateValue;

        if (context.UseWebRequested && ShouldForceWebSearch(context.RoutingQuestion, context))
        {
            context.QueryClassification = "tool_intent_request";
            context.QueryClassificationReason = "Use-web request with freshness/current-event language; routing to governed MCP read-only web search.";
            context.IntentType = "search.web";
            context.AudienceMode = null;
            context.VerbosityMode = "concise";
            context.RetrievalAllowed = false;
            context.GroundingMode = "tool_intent";
            context.RetrievalDomain = "web";
            context.RetrievalDomainTerms = new[] { "search", "web", "current" };
            context.RetrievalSkippedReason = "mcp_read_only_web_search_requested";
            context.RetrievalMinScore = 0.35f;
            context.EvidenceRequired = false;
            context.MemoryUpdateKind = null;
            context.MemoryUpdateValue = null;
        }

        await _journeyEmitter.EmitAsync(
            "query_classifier",
            "completed",
            fromComponent: "cwi-orchestrator",
            toComponent: "query_classifier",
            metadata: new Dictionary<string, object?>
            {
                ["classification"] = classification.Classification,
                ["reason"] = classification.Reason,
                ["intent_type"] = classification.IntentType,
                ["audience_mode"] = classification.AudienceMode,
                ["verbosity_mode"] = classification.VerbosityMode,
                ["retrieval_allowed"] = classification.RetrievalAllowed,
                ["grounding_mode"] = classification.GroundingMode,
                ["retrieval_domain"] = classification.RetrievalDomain,
                ["retrieval_skipped_reason"] = classification.RetrievalSkippedReason,
                ["evidence_required"] = classification.EvidenceRequired,
                ["normalization_applied"] = context.NormalizationApplied,
                ["normalization_rules"] = context.NormalizationRules,
                ["normalization_failed"] = context.NormalizationFailed
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await next().ConfigureAwait(false);
    }

    private async Task<ServiceResponse<AskResponseDto>> ApplyClarificationFallbackAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        string journeyMessage,
        CancellationToken cancellationToken)
    {
        context.Answer = "I did not catch that clearly. Can you rephrase the question?";
        context.Route = "clarification_handler";
        context.QueryClassification = "clarification";
        context.QueryClassificationReason = "Language normalization or intent classification failed; asking for clarification instead of falling back to a domain pack.";
        context.IntentType = "clarification_intent";
        context.TemplateUsed = "clarification";
        context.ModelName = "clarification-handler";
        context.SkipModelInference = true;
        context.ExecutionTarget = "deterministic_runtime";
        context.GroundingMode = "non_grounded";
        context.RetrievalAllowed = false;
        context.RetrievalSkippedReason = "classification_failed_requires_clarification";
        context.EvidenceRequired = false;
        context.FallbackApplied = true;
        await _journeyEmitter.EmitAsync(
            "query_classifier",
            "failed",
            fromComponent: "cwi-orchestrator",
            toComponent: "query_classifier",
            message: journeyMessage,
            metadata: new Dictionary<string, object?>
            {
                ["route"] = context.Route,
                ["fallback_confidence"] = 0.45f,
                ["normalization_applied"] = context.NormalizationApplied,
                ["normalization_rules"] = context.NormalizationRules,
                ["normalization_failed"] = context.NormalizationFailed,
                ["normalization_failure_reason"] = context.NormalizationFailureReason
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return await next().ConfigureAwait(false);
    }

    private static bool ShouldForceWebSearch(string question, AskWorkflowContext context)
    {
        if (string.Equals(context.QueryClassification, "deterministic_reasoning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "deterministic_datetime", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "identity_definition", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "home_node_definition", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "needs_clarification", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(context.QueryClassification, "tool_intent_request", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(context.IntentType, "search.web", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var normalized = Cwi.Orchestrator.Application.Ask.LanguageNormalizer.NormalizeForRouting(question);
        return normalized.Contains("current", StringComparison.Ordinal)
               || normalized.Contains("latest", StringComparison.Ordinal)
               || normalized.Contains("right now", StringComparison.Ordinal)
               || normalized.Contains("today", StringComparison.Ordinal)
               || normalized.Contains("2026", StringComparison.Ordinal)
               || normalized.Contains("use the web", StringComparison.Ordinal)
               || normalized.Contains("find out", StringComparison.Ordinal)
               || normalized.Contains("population of", StringComparison.Ordinal)
               || normalized.Contains("population in", StringComparison.Ordinal)
               || normalized.Contains("how many people live", StringComparison.Ordinal);
    }
}

public sealed class AskPreferenceMemoryStep : IAskWorkflowStep
{
    private readonly OperatorPreferenceMemory _preferenceMemory;
    private readonly JourneyEmitter _journeyEmitter;

    public AskPreferenceMemoryStep(
        OperatorPreferenceMemory preferenceMemory,
        JourneyEmitter journeyEmitter)
    {
        _preferenceMemory = preferenceMemory;
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (string.Equals(context.QueryClassification, "preference_update", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(context.MemoryUpdateKind)
            && !string.IsNullOrWhiteSpace(context.MemoryUpdateValue))
        {
            var preference = _preferenceMemory.Update(
                context.TenantId,
                context.UserId,
                context.MemoryUpdateKind,
                context.MemoryUpdateValue,
                AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion));

            context.MemoryUpdated = true;
            context.SkipModelInference = true;
            context.RetrievalAllowed = false;
            context.GroundingMode = "memory_update";
            context.RetrievalSkippedReason = "preference_update_routes_to_memory";
            context.ExecutionTarget = "operator_memory";
            context.Answer = BuildPreferenceAcknowledgement(preference);
            context.Retrieved.Clear();
            context.GraphEvidence.Clear();
            context.MemoryResults = Array.Empty<GovernedMemoryResult>();
            context.SourceResults = Array.Empty<GovernedSourceResult>();

            await _journeyEmitter.EmitAsync(
                "operator_memory",
                "preference_updated",
                fromComponent: "cwi-orchestrator",
                toComponent: "operator_memory",
                metadata: new Dictionary<string, object?>
                {
                    ["preference_key"] = preference.Key,
                    ["preference_value"] = preference.Value,
                    ["grounding_mode"] = context.GroundingMode
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return await next().ConfigureAwait(false);
        }

        if (IsGovernedMemoryUpdate(context)
            && !string.IsNullOrWhiteSpace(context.MemoryUpdateValue))
        {
            context.SkipModelInference = true;
            context.RetrievalAllowed = false;
            context.GroundingMode = "memory_update";
            context.RetrievalSkippedReason = $"{context.QueryClassification}_routes_to_memory";
            context.ExecutionTarget = "governed_memory";
            context.Answer = BuildGovernedMemoryAcknowledgement(context);
            context.Retrieved.Clear();
            context.GraphEvidence.Clear();
            context.MemoryResults = Array.Empty<GovernedMemoryResult>();
            context.SourceResults = Array.Empty<GovernedSourceResult>();

            await _journeyEmitter.EmitAsync(
                "governed_memory",
                "memory_update_accepted",
                fromComponent: "cwi-orchestrator",
                toComponent: "cwi-memory-capsule",
                metadata: new Dictionary<string, object?>
                {
                    ["query_classification"] = context.QueryClassification,
                    ["memory_update_kind"] = context.MemoryUpdateKind,
                    ["grounding_mode"] = context.GroundingMode
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return await next().ConfigureAwait(false);
        }

        var outputStyle = _preferenceMemory.Get(context.TenantId, context.UserId, "output_style");
        if (outputStyle is not null
            && string.IsNullOrWhiteSpace(context.VerbosityMode)
            && CanApplyStoredPreference(context))
        {
            context.VerbosityMode = outputStyle.Value;
            context.AppliedPreferenceMode = outputStyle.Value;
        }

        return await next().ConfigureAwait(false);
    }

    private static bool IsGovernedMemoryUpdate(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "self_context_update", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.QueryClassification, "controlled_fact_update", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPreferenceAcknowledgement(OperatorPreference preference)
    {
        return preference.Value switch
        {
            "step_by_step" => "Preference saved. I will use step-by-step instructions when that format fits the request.",
            "concise" => "Preference saved. I will keep answers concise when that format fits the request.",
            "detailed" => "Preference saved. I will use detailed answers when that format fits the request.",
            _ => "Preference saved. I will use that output preference when it fits the request."
        };
    }

    private static string BuildGovernedMemoryAcknowledgement(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "controlled_fact_update", StringComparison.OrdinalIgnoreCase)
            ? "Controlled fact recorded in governed memory."
            : "Got it. I saved that context in governed memory.";
    }

    private static bool CanApplyStoredPreference(AskWorkflowContext context)
    {
        if (context.EvidenceRequired
            || !string.IsNullOrWhiteSpace(context.AudienceMode)
            || string.Equals(context.QueryClassification, "preference_update", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "deterministic_reasoning", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "identity_definition", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "home_node_definition", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "memory_update", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "troubleshooting", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "decision_support", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "operational", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}

public sealed class AskCoreGovernanceStep : IAskWorkflowStep
{
    private static readonly string[] SecretExposureTerms =
    {
        "api key",
        "apikey",
        "password",
        "private key",
        "secret token",
        "access token",
        "credentials"
    };

    private static readonly string[] SecretActionTerms =
    {
        "show",
        "print",
        "reveal",
        "dump",
        "expose",
        "send",
        "share"
    };

    private readonly JourneyEmitter _journeyEmitter;

    public AskCoreGovernanceStep(JourneyEmitter journeyEmitter)
    {
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var packGuidance = context.BaselinePackGuidance
            .ToDictionary(pack => pack.PackName, pack => pack, StringComparer.OrdinalIgnoreCase);

        foreach (var packName in CorePackOrdering.CorePackOrder)
        {
            if (!packGuidance.TryGetValue(packName, out var guidance))
            {
                return ServiceResponse<AskResponseDto>.Fail(
                    new DomainError(ErrorCodes.DependencyUnavailable, $"Required core pack unavailable: {packName}."),
                    statusCode: 503,
                    message: "Required core pack unavailable");
            }

            await _journeyEmitter.EmitAsync(
                "core_pack",
                "applied",
                fromComponent: "cwi-orchestrator",
                toComponent: packName,
                metadata: new Dictionary<string, object?>
                {
                    ["pack_id"] = guidance.PackName,
                    ["pack_version"] = guidance.PackVersion,
                    ["pack_hash"] = guidance.PackHash,
                    ["query_classification"] = context.QueryClassification,
                    ["intent_type"] = context.IntentType
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        if (ShouldBlockSecretExposure(AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion)))
        {
            context.GuardrailViolations = new[]
            {
                new GuardrailViolation
                {
                    Id = "secret_exposure",
                    Name = "Secret exposure prevention",
                    Severity = "high",
                    Message = "The request asks to reveal or expose sensitive credentials."
                }
            };

            return ServiceResponse<AskResponseDto>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Safety guardrails blocked credential exposure."),
                statusCode: 403,
                message: "Safety guardrails blocked credential exposure.");
        }

        return await next().ConfigureAwait(false);
    }

    private static bool ShouldBlockSecretExposure(string question)
    {
        var normalized = question.Trim().ToLowerInvariant();
        return SecretExposureTerms.Any(term => normalized.Contains(term, StringComparison.Ordinal))
               && SecretActionTerms.Any(term => normalized.Contains(term, StringComparison.Ordinal));
    }
}

public sealed class AskCommunicationRuntimeStep : IAskWorkflowStep
{
    private readonly JourneyEmitter _journeyEmitter;
    private readonly ComprehensionExpressionFrameworkService _comprehensionExpressionFramework;

    public AskCommunicationRuntimeStep(
        JourneyEmitter journeyEmitter,
        ComprehensionExpressionFrameworkService? comprehensionExpressionFramework = null)
    {
        _journeyEmitter = journeyEmitter;
        _comprehensionExpressionFramework = comprehensionExpressionFramework ?? new ComprehensionExpressionFrameworkService();
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        ApplyIntentDefaultsBeforeTone(context);
        var expressionDecision = _comprehensionExpressionFramework.Evaluate(context);
        ApplyComprehensionExpressionDecision(context, expressionDecision);
        context.ToneMode = ResolveToneMode(context);
        if (ShouldApplyToneOverlay(context, expressionDecision))
        {
            context.ToneMode = expressionDecision.ToneProfile;
        }

        context.TemplateUsed = ResolveTemplate(context);
        context.CommunicationRuntimeApplied = true;

        if (IsFailClosed(context)
            && !context.RetrievalAllowed)
        {
            context.RetrievalSkippedReason ??= "evidence_required_but_missing_domain_context";
        }

        await _journeyEmitter.EmitAsync(
            "communication_runtime",
            "completed",
            fromComponent: "cwi-orchestrator",
            toComponent: "communication_runtime",
            metadata: new Dictionary<string, object?>
            {
                ["intent_type"] = context.IntentType,
                ["audience_mode"] = context.AudienceMode,
                ["verbosity_mode"] = context.VerbosityMode,
                ["tone_mode"] = context.ToneMode,
                ["context_awareness_mode"] = context.ContextAwarenessMode,
                ["clarity_decision"] = context.ClarityDecision,
                ["expression_tone_profile"] = context.ExpressionToneProfile,
                ["expression_tone_reason"] = context.ExpressionToneReason,
                ["template_used"] = context.TemplateUsed,
                ["retrieval_allowed"] = context.RetrievalAllowed,
                ["evidence_required"] = context.EvidenceRequired
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await next().ConfigureAwait(false);
    }

    private static void ApplyComprehensionExpressionDecision(
        AskWorkflowContext context,
        ComprehensionExpressionDecision decision)
    {
        context.ComprehensionExpressionVersion = decision.FrameworkVersion;
        context.ContextAwarenessMode = decision.ContextMode;
        context.ClarityDecision = decision.ClarityDecision;
        context.ClarityReason = decision.ClarityReason;
        context.ExpressionToneProfile = decision.ToneProfile;
        context.ExpressionToneReason = decision.ToneReason;

        if (!string.IsNullOrWhiteSpace(decision.AudienceMode))
        {
            context.AudienceMode ??= decision.AudienceMode;
        }

        if (!string.IsNullOrWhiteSpace(decision.VerbosityMode))
        {
            context.VerbosityMode ??= decision.VerbosityMode;
        }
    }

    private static bool ShouldApplyToneOverlay(
        AskWorkflowContext context,
        ComprehensionExpressionDecision decision)
    {
        return string.Equals(decision.ToneReason, "tone_safety_override", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyIntentDefaultsBeforeTone(AskWorkflowContext context)
    {
        if (string.Equals(context.IntentType, "troubleshooting", StringComparison.OrdinalIgnoreCase))
        {
            context.VerbosityMode ??= "step_by_step";
            if (!context.EvidenceRequired)
            {
                context.RetrievalAllowed = false;
                context.GroundingMode = "non_grounded";
                context.RetrievalSkippedReason ??= "troubleshooting_without_explicit_source_request";
            }
        }

        if (string.Equals(context.AudienceMode, "executive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "decision_support", StringComparison.OrdinalIgnoreCase))
        {
            context.AudienceMode ??= "executive";
            context.VerbosityMode ??= "concise";
        }

        if (string.Equals(context.IntentType, "risk_review", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "risk_analysis", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "daily_briefing", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "change_review", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "plan_request", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "planning", StringComparison.OrdinalIgnoreCase))
        {
            context.VerbosityMode = "concise";
        }
    }

    private static string ResolveToneMode(AskWorkflowContext context)
    {
        if (string.Equals(context.QueryClassification, "preference_update", StringComparison.OrdinalIgnoreCase))
        {
            return "confirmation";
        }

        if (string.Equals(context.QueryClassification, "self_context_update", StringComparison.OrdinalIgnoreCase))
        {
            return "confirmation";
        }

        if (string.Equals(context.QueryClassification, "identity_definition", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "home_node_definition", StringComparison.OrdinalIgnoreCase))
        {
            return "clear_system_truth";
        }

        if (GovernedResponseModes.TryResolveStructuredMode(context, out _, out var structuredTone))
        {
            return structuredTone;
        }

        if (string.Equals(context.IntentType, "needs_clarification", StringComparison.OrdinalIgnoreCase))
        {
            return "clarification";
        }

        if (string.Equals(context.IntentType, "summarization", StringComparison.OrdinalIgnoreCase))
        {
            return "plain_concise";
        }

        if (string.Equals(context.IntentType, "code_generation", StringComparison.OrdinalIgnoreCase))
        {
            return "technical_direct";
        }

        if (IsToolIntent(context.IntentType))
        {
            return "tool_ready";
        }

        if (IsFailClosed(context))
        {
            return "careful_escalation";
        }

        if (string.Equals(context.IntentType, "troubleshooting", StringComparison.OrdinalIgnoreCase))
        {
            return "direct_step_by_step";
        }

        if (string.Equals(context.AudienceMode, "executive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "decision_support", StringComparison.OrdinalIgnoreCase))
        {
            return "executive_concise";
        }

        return string.Equals(context.VerbosityMode, "step_by_step", StringComparison.OrdinalIgnoreCase)
            ? "instructional"
            : "plain_direct";
    }

    private static string ResolveTemplate(AskWorkflowContext context)
    {
        if (string.Equals(context.QueryClassification, "preference_update", StringComparison.OrdinalIgnoreCase))
        {
            return "memory_acknowledgement";
        }

        if (string.Equals(context.QueryClassification, "self_context_update", StringComparison.OrdinalIgnoreCase))
        {
            return "memory_acknowledgement";
        }

        if (string.Equals(context.QueryClassification, "deterministic_reasoning", StringComparison.OrdinalIgnoreCase))
        {
            return "direct_answer";
        }

        if (string.Equals(context.QueryClassification, "identity_definition", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.QueryClassification, "home_node_definition", StringComparison.OrdinalIgnoreCase))
        {
            return "system_truth_answer";
        }

        if (GovernedResponseModes.TryResolveStructuredMode(context, out var structuredTemplate, out _))
        {
            return structuredTemplate;
        }

        if (string.Equals(context.IntentType, "needs_clarification", StringComparison.OrdinalIgnoreCase))
        {
            return "clarification";
        }

        if (string.Equals(context.IntentType, "summarization", StringComparison.OrdinalIgnoreCase))
        {
            return "summary_answer";
        }

        if (string.Equals(context.IntentType, "code_generation", StringComparison.OrdinalIgnoreCase))
        {
            return "code_answer";
        }

        if (IsToolIntent(context.IntentType))
        {
            return "tool_readiness";
        }

        if (IsFailClosed(context))
        {
            return "low_confidence_escalation";
        }

        if (string.Equals(context.IntentType, "troubleshooting", StringComparison.OrdinalIgnoreCase))
        {
            return "operator_troubleshooting";
        }

        if (string.Equals(context.AudienceMode, "executive", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "decision_support", StringComparison.OrdinalIgnoreCase))
        {
            return "executive_summary";
        }

        if (string.Equals(context.IntentType, "operational", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.VerbosityMode, "step_by_step", StringComparison.OrdinalIgnoreCase))
        {
            return "operator_steps";
        }

        return "general_answer";
    }

    private static bool IsFailClosed(AskWorkflowContext context)
    {
        return context.EvidenceRequired && !context.RetrievalAllowed;
    }

    private static bool IsToolIntent(string? intentType)
    {
        return string.Equals(intentType, "weather.get", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "news.get", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "sports.get", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "search.web", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "system.status", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "node.health.check", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "ledger.view", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "pack.list", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "pack.get", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "pin.resolve", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "calendar.view", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "email.read", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "email.send", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "workflow.start", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class AskPackCapabilityStep : IAskWorkflowStep
{
    private readonly PackCapabilityPolicyEvaluator _policyEvaluator;
    private readonly IApprovalTicketRepository _approvalTicketRepository;

    public AskPackCapabilityStep(
        PackCapabilityPolicyEvaluator policyEvaluator,
        IApprovalTicketRepository approvalTicketRepository)
    {
        _policyEvaluator = policyEvaluator;
        _approvalTicketRepository = approvalTicketRepository;
    }

    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        return ExecuteInternalAsync(context, next, cancellationToken);
    }

    private async Task<ServiceResponse<AskResponseDto>> ExecuteInternalAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var capabilities = context.PackCapabilities?.Capabilities ?? Array.Empty<PackCapability>();
        if (capabilities.Count == 0)
        {
            return await next().ConfigureAwait(false);
        }

        var decision = _policyEvaluator.Evaluate(context.PolicyMode, capabilities);
        if (!decision.IsSuccess || decision.Data is null)
        {
            return ServiceResponse<AskResponseDto>.Fail(
                decision.Error ?? new DomainError(ErrorCodes.InvalidRequest, "Invalid policy."),
                statusCode: decision.StatusCode,
                message: decision.Message);
        }

        context.PackCapabilityDecision = decision.Data;

        if (decision.Data.RequiresApproval)
        {
            if (string.IsNullOrWhiteSpace(context.TraceId))
            {
                context.TraceId = Guid.NewGuid().ToString("N");
            }

            return await CreateApprovalTicketAsync(context, decision.Data, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!decision.Data.AllowExecution)
        {
            if (string.IsNullOrWhiteSpace(context.TraceId))
            {
                context.TraceId = Guid.NewGuid().ToString("N");
            }

            return ServiceResponse<AskResponseDto>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Pack capability blocked."),
                statusCode: 403,
                message: "Pack capability blocked");
        }

        return await next().ConfigureAwait(false);
    }

    private async Task<ServiceResponse<AskResponseDto>> CreateApprovalTicketAsync(
        AskWorkflowContext context,
        PackCapabilityDecision decision,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = $"pack-capability:{context.TenantId}:{context.UserId}:{context.PackName}:{context.PackHash}:{context.PolicyMode}";
        var existing = await _approvalTicketRepository
            .GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return ServiceResponse<AskResponseDto>.Fail(
                new DomainError(ErrorCodes.Forbidden, $"Pack capability requires approval. Ticket: {existing.TicketId}."),
                statusCode: 403,
                message: "Pack capability requires approval");
        }

        var ticketId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var ticket = new ApprovalTicket
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            TenantId = context.TenantId,
            UserId = context.UserId,
            Risk = decision.Risk.ToString().ToLowerInvariant(),
            Reason = decision.Reason ?? "Pack capability requires approval.",
            Status = "pending",
            IdempotencyKey = idempotencyKey,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _approvalTicketRepository.AddAsync(ticket, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<AskResponseDto>.Fail(
            new DomainError(ErrorCodes.Forbidden, $"Pack capability requires approval. Ticket: {ticketId}."),
            statusCode: 403,
            message: "Pack capability requires approval");
    }
}

public sealed class AskPolicyModeStep : IAskWorkflowStep
{
    private readonly PolicyDefaultsResolver _policyDefaultsResolver;

    public AskPolicyModeStep(PolicyDefaultsResolver policyDefaultsResolver)
    {
        _policyDefaultsResolver = policyDefaultsResolver;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var resolved = await _policyDefaultsResolver
            .ResolveAsync(context.TenantId, context.PolicyMode, context.PackDocument, cancellationToken)
            .ConfigureAwait(false);

        if (!resolved.IsSuccess || resolved.Data is null)
        {
            return ServiceResponse<AskResponseDto>.Fail(
                resolved.Error ?? new DomainError(ErrorCodes.InternalError, "Policy defaults unavailable."),
                statusCode: resolved.StatusCode,
                message: resolved.Message);
        }

        context.PolicyMode = resolved.Data;
        return await next().ConfigureAwait(false);
    }
}

public sealed class AskOperatorProfileStep : IAskWorkflowStep
{
    private readonly OperatorProfileService _profileService;
    private readonly OperatorProfilePolicyService _policyService;
    private readonly OrchestratorMetrics _metrics;

    public AskOperatorProfileStep(
        OperatorProfileService profileService,
        OperatorProfilePolicyService policyService,
        OrchestratorMetrics metrics)
    {
        _profileService = profileService;
        _policyService = policyService;
        _metrics = metrics;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var profileResult = await _profileService
            .GetAsync(context.TenantId, context.UserId, cancellationToken)
            .ConfigureAwait(false);

        if (profileResult.IsSuccess && profileResult.Data is not null)
        {
            context.OperatorProfile = profileResult.Data;
            var decision = _policyService.Evaluate(context.PolicyMode, profileResult.Data);
            context.OperatorProfileDecision = decision;
            _metrics.RecordOperatorProfileDecision(decision);
            if (decision.Applied)
            {
                context.PolicyMode = decision.PolicyMode;
            }
        }

        return await next().ConfigureAwait(false);
    }
}

public sealed class AskRetrievalStep : IAskWorkflowStep
{
    private readonly IRetrievalProvider _retrievalProvider;
    private readonly JourneyEmitter _journeyEmitter;

    public AskRetrievalStep(IRetrievalProvider retrievalProvider, JourneyEmitter journeyEmitter)
    {
        _retrievalProvider = retrievalProvider;
        _journeyEmitter = journeyEmitter;
    }

    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        return ExecuteInternalAsync(context, next, cancellationToken);
    }

    private async Task<ServiceResponse<AskResponseDto>> ExecuteInternalAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (!context.RetrievalAllowed)
        {
            context.Retrieved.Clear();
            await _journeyEmitter.EmitAsync(
                "vector_retrieval",
                "skipped",
                fromComponent: "cwi-orchestrator",
                toComponent: "vector_retrieval",
                message: context.RetrievalSkippedReason,
                metadata: new Dictionary<string, object?>
                {
                    ["query_classification"] = context.QueryClassification,
                    ["grounding_mode"] = context.GroundingMode,
                    ["retrieval_domain"] = context.RetrievalDomain
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return await next().ConfigureAwait(false);
        }

        var started = DateTimeOffset.UtcNow;
        await _journeyEmitter.EmitAsync(
            "vector_retrieval",
            "started",
            fromComponent: "cwi-orchestrator",
            toComponent: "vector_retrieval",
            metadata: new Dictionary<string, object?>
            {
                ["collection"] = context.PackName,
                ["top_k"] = context.TopK,
                ["pack_hash"] = context.PackHash,
                ["pack_version"] = context.PackVersion,
                ["retrieval_domain"] = context.RetrievalDomain,
                ["domain_terms"] = context.RetrievalDomainTerms
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var retrievalFilter = new RetrievalFilter
        {
            PackId = context.PackName,
            PackHash = context.PackHash,
            PackVersion = context.PackVersion
        };
        var retrieval = await _retrievalProvider
            .RetrieveAsync(context.PackName, AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion), context.TopK, retrievalFilter, cancellationToken)
            .ConfigureAwait(false);
        await _journeyEmitter.EmitAsync(
            "vector_retrieval",
            retrieval.IsSuccess && retrieval.Data is not null ? "completed" : "failed",
            direction: "response",
            fromComponent: "vector_retrieval",
            toComponent: "cwi-orchestrator",
            statusCode: retrieval.StatusCode,
            durationMs: (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            message: retrieval.Message,
            metadata: new Dictionary<string, object?>
            {
                ["result_count"] = retrieval.Data?.Count ?? 0,
                ["collection"] = context.PackName,
                ["pack_hash"] = context.PackHash,
                ["pack_version"] = context.PackVersion
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!retrieval.IsSuccess || retrieval.Data is null)
        {
            context.RetrievalUnavailable = true;
            context.Retrieved.Clear();
            return await next().ConfigureAwait(false);
        }

        var filtered = AskEvidencePolicy.FilterRetrievalResults(context, retrieval.Data);
        context.Retrieved.Clear();
        context.Retrieved.AddRange(filtered);
        if (retrieval.Data.Count > 0 && filtered.Count == 0)
        {
            context.RetrievalSkippedReason = "retrieval_results_failed_domain_or_score_policy";
        }

        return await next().ConfigureAwait(false);
    }
}

public sealed class AskDocumentMemoryRetrievalStep : IAskWorkflowStep
{
    private readonly GovernedMemoryService _memoryService;
    private readonly JourneyEmitter _journeyEmitter;

    public AskDocumentMemoryRetrievalStep(GovernedMemoryService memoryService, JourneyEmitter journeyEmitter)
    {
        _memoryService = memoryService;
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var attemptedUploadedContextLookup = false;
        UploadedContextLookupSnapshot? uploadedContextLookupSnapshot = null;
        if (!AskEvidencePolicy.ShouldUseRetrievedEvidence(context)
            && AskUploadedContextLookupPolicy.TryApply(context, out var snapshot))
        {
            attemptedUploadedContextLookup = true;
            uploadedContextLookupSnapshot = snapshot;
        }

        if (!AskEvidencePolicy.ShouldUseRetrievedEvidence(context))
        {
            context.MemoryResults = Array.Empty<GovernedMemoryResult>();
            context.SourceResults = Array.Empty<GovernedSourceResult>();
            await _journeyEmitter.EmitAsync(
                "document_memory_retrieval",
                "skipped",
                fromComponent: "cwi-orchestrator",
                toComponent: "cwi-memory-capsule",
                message: context.RetrievalSkippedReason,
                metadata: new Dictionary<string, object?>
                {
                    ["query_classification"] = context.QueryClassification,
                    ["grounding_mode"] = context.GroundingMode,
                    ["retrieval_domain"] = context.RetrievalDomain
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return await next().ConfigureAwait(false);
        }

        var started = DateTimeOffset.UtcNow;
        await _journeyEmitter.EmitAsync(
            "document_memory_retrieval",
            "started",
            fromComponent: "cwi-orchestrator",
            toComponent: "cwi-memory-capsule",
            metadata: new Dictionary<string, object?>
            {
                ["tenant_id"] = context.TenantId,
                ["top_k"] = context.TopK,
                ["retrieval_domain"] = context.RetrievalDomain,
                ["domain_terms"] = context.RetrievalDomainTerms
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        try
        {
            var retrieval = await _memoryService.RetrieveAsync(context, cancellationToken).ConfigureAwait(false);
            if (!retrieval.IsSuccess || retrieval.Data is null)
            {
                context.MemoryUnavailable = true;
                context.SourceRetrievalUnavailable = true;
                return await next().ConfigureAwait(false);
            }

            var memoryResults = AskEvidencePolicy.FilterMemoryResults(context, retrieval.Data.MemoryResults);
            var sourceResults = AskEvidencePolicy.FilterSourceResults(context, retrieval.Data.SourceResults);
            context.MemoryResults = memoryResults;
            context.SourceResults = sourceResults;

            if ((retrieval.Data.MemoryResults.Count > 0 && memoryResults.Count == 0)
                || (retrieval.Data.SourceResults.Count > 0 && sourceResults.Count == 0))
            {
                context.RetrievalSkippedReason ??= "memory_or_source_results_failed_domain_or_score_policy";
            }

            if (attemptedUploadedContextLookup
                && memoryResults.Count == 0
                && sourceResults.Count == 0
                && uploadedContextLookupSnapshot is not null)
            {
                AskUploadedContextLookupPolicy.Restore(context, uploadedContextLookupSnapshot);
                context.MemoryResults = Array.Empty<GovernedMemoryResult>();
                context.SourceResults = Array.Empty<GovernedSourceResult>();
                context.Retrieved.Clear();
            }

            var existingKeys = context.Retrieved
                .Select(result => $"{result.DocId}:{result.ChunkId}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var result in sourceResults)
            {
                if (existingKeys.Add($"{result.SourceId}:{result.ChunkId}"))
                {
                    context.Retrieved.Add(new RetrievalResult(
                        result.SourceId,
                        result.ChunkId,
                        result.Text,
                        result.RelevanceScore));
                }
            }

            await _journeyEmitter.EmitAsync(
                "document_memory_retrieval",
                "completed",
                direction: "response",
                fromComponent: "cwi-memory-capsule",
                toComponent: "cwi-orchestrator",
                durationMs: (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
                metadata: new Dictionary<string, object?>
                {
                    ["memory_result_count"] = retrieval.Data.MemoryResults.Count,
                    ["accepted_memory_count"] = memoryResults.Count,
                    ["source_result_count"] = retrieval.Data.SourceResults.Count,
                    ["accepted_source_count"] = sourceResults.Count,
                    ["merged_result_count"] = context.Retrieved.Count
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.MemoryUnavailable = true;
            context.SourceRetrievalUnavailable = true;
        }

        return await next().ConfigureAwait(false);
    }
}

public sealed class AskGraphRetrievalStep : IAskWorkflowStep
{
    private readonly IGraphEvidenceProvider _graphEvidenceProvider;
    private readonly JourneyEmitter _journeyEmitter;

    public AskGraphRetrievalStep(IGraphEvidenceProvider graphEvidenceProvider, JourneyEmitter journeyEmitter)
    {
        _graphEvidenceProvider = graphEvidenceProvider;
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (!context.RetrievalAllowed)
        {
            context.GraphEvidence.Clear();
            context.GraphQueryTemplate = null;
            context.GraphQueryTerms = Array.Empty<string>();
            await _journeyEmitter.EmitAsync(
                "graph_retrieval",
                "skipped",
                fromComponent: "cwi-orchestrator",
                toComponent: "graphdb",
                message: context.RetrievalSkippedReason,
                metadata: new Dictionary<string, object?>
                {
                    ["query_classification"] = context.QueryClassification,
                    ["grounding_mode"] = context.GroundingMode,
                    ["retrieval_domain"] = context.RetrievalDomain
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return await next().ConfigureAwait(false);
        }

        var started = DateTimeOffset.UtcNow;
        await _journeyEmitter.EmitAsync(
            "graph_retrieval",
            "started",
            fromComponent: "cwi-orchestrator",
            toComponent: "graphdb",
            metadata: new Dictionary<string, object?>
            {
                ["question"] = AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion),
                ["retrieval_domain"] = context.RetrievalDomain,
                ["domain_terms"] = context.RetrievalDomainTerms
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var graphResult = await _graphEvidenceProvider
            .RetrieveAsync(new GraphEvidenceRequest(
                AskQuestionScope.ExtractPrimaryQuestion(context.RoutingQuestion),
                context.TenantId,
                context.UserId,
                context.TopK), cancellationToken)
            .ConfigureAwait(false);

        context.GraphEvidence.Clear();
        context.GraphQueryTemplate = graphResult.Data?.Template;
        context.GraphQueryTerms = graphResult.Data?.Terms ?? Array.Empty<string>();
        await _journeyEmitter.EmitAsync(
            "graph_retrieval",
            graphResult.IsSuccess && graphResult.Data is not null ? "completed" : "failed",
            direction: "response",
            fromComponent: "graphdb",
            toComponent: "cwi-orchestrator",
            statusCode: graphResult.StatusCode,
            durationMs: (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            message: graphResult.Message,
            metadata: new Dictionary<string, object?>
            {
                ["template"] = graphResult.Data?.Template,
                ["evidence_count"] = graphResult.Data?.Evidence.Count ?? 0
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!graphResult.IsSuccess || graphResult.Data is null)
        {
            context.GraphUnavailable = true;
            context.GraphFailureReason = graphResult.Message;
            return await next().ConfigureAwait(false);
        }

        var filtered = AskEvidencePolicy.FilterGraphEvidence(context, graphResult.Data.Evidence);
        context.GraphEvidence.AddRange(filtered);
        if (graphResult.Data.Evidence.Count > 0 && filtered.Count == 0)
        {
            context.RetrievalSkippedReason = "graph_results_failed_domain_policy";
        }

        return await next().ConfigureAwait(false);
    }
}

public sealed class AskEvidenceRequirementStep : IAskWorkflowStep
{
    private readonly JourneyEmitter _journeyEmitter;

    public AskEvidenceRequirementStep(JourneyEmitter journeyEmitter)
    {
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (!context.EvidenceRequired || AskEvidencePolicy.HasGroundedEvidence(context))
        {
            return await next().ConfigureAwait(false);
        }

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
        context.ModelName = "governed-evidence-gate";
        context.SkipModelInference = true;
        context.FallbackApplied = true;
        context.RetrievalAllowed = false;
        context.RetrievalSkippedReason ??= "evidence_required_but_no_governed_evidence";
        AddWarning(context, "evidence_required_but_no_governed_evidence");
        context.Retrieved.Clear();
        context.GraphEvidence.Clear();
        context.MemoryResults = Array.Empty<GovernedMemoryResult>();
        context.SourceResults = Array.Empty<GovernedSourceResult>();

        await _journeyEmitter.EmitAsync(
            "evidence_requirement",
            "blocked",
            fromComponent: "cwi-orchestrator",
            toComponent: "evidence_policy",
            message: context.RetrievalSkippedReason,
            metadata: new Dictionary<string, object?>
            {
                ["query_classification"] = context.QueryClassification,
                ["intent_type"] = context.IntentType,
                ["template_used"] = context.TemplateUsed,
                ["retrieval_domain"] = context.RetrievalDomain,
                ["domain_terms"] = context.RetrievalDomainTerms
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await next().ConfigureAwait(false);
    }

    private static void AddWarning(AskWorkflowContext context, string warning)
    {
        if (!context.ResponseWarnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
        {
            context.ResponseWarnings.Add(warning);
        }
    }
}

public sealed class AskTrustedWebContextStep : IAskWorkflowStep
{
    private readonly TrustedWebContextOptions _options;
    private readonly ITrustedWebContextProvider _provider;
    private readonly JourneyEmitter _journeyEmitter;

    public AskTrustedWebContextStep(
        TrustedWebContextOptions options,
        ITrustedWebContextProvider provider,
        JourneyEmitter journeyEmitter)
    {
        _options = options;
        _provider = provider;
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (!context.TrustedWebContextRequested)
        {
            return await next().ConfigureAwait(false);
        }

        if (context.SkipModelInference)
        {
            MarkSkipped(context, "trusted_web_native_path_completed_before_prompt");
            return await next().ConfigureAwait(false);
        }

        if (!_options.Enabled)
        {
            MarkSkipped(context, "trusted_web_context_disabled");
            return await next().ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(context.TrustedWebContextMode)
            && !string.Equals(context.TrustedWebContextMode, "include", StringComparison.OrdinalIgnoreCase))
        {
            MarkSkipped(context, "trusted_web_context_mode_unsupported");
            return await next().ConfigureAwait(false);
        }

        var started = DateTimeOffset.UtcNow;
        await _journeyEmitter.EmitAsync(
            "trusted_web_context",
            "started",
            fromComponent: "cwi-orchestrator",
            toComponent: "trusted_web_context",
            metadata: new Dictionary<string, object?>
            {
                ["profile"] = context.TrustedWebContextProfile ?? _options.DefaultProfile,
                ["max_results"] = context.TrustedWebContextMaxResults ?? _options.MaxResults
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await _provider.RetrieveAsync(
                new TrustedWebContextRequest(
                    context.RoutingQuestion,
                    context.TrustedWebContextProfile,
                    context.TrustedWebContextMaxResults,
                    context.TenantId),
                cancellationToken).ConfigureAwait(false);
            var durationMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;
            context.TrustedWebContextEnabled = true;
            context.TrustedWebContextProfile = result.Profile;
            context.TrustedWebResults = result.Results;
            context.TrustedWebContextRetrievalDurationMs = durationMs;
            if (!string.IsNullOrWhiteSpace(result.SkippedReason))
            {
                MarkSkipped(context, result.SkippedReason);
            }

            await _journeyEmitter.EmitAsync(
                "trusted_web_context",
                string.IsNullOrWhiteSpace(result.SkippedReason) ? "completed" : "skipped",
                direction: "response",
                fromComponent: "trusted_web_context",
                toComponent: "cwi-orchestrator",
                durationMs: durationMs,
                message: result.SkippedReason,
                metadata: new Dictionary<string, object?>
                {
                    ["profile"] = result.Profile,
                    ["result_count"] = result.Results.Count,
                    ["skipped_reason"] = result.SkippedReason
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var durationMs = (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds;
            context.TrustedWebContextRetrievalDurationMs = durationMs;
            MarkSkipped(context, "trusted_web_context_unavailable");
            await _journeyEmitter.EmitAsync(
                "trusted_web_context",
                "failed",
                direction: "response",
                fromComponent: "trusted_web_context",
                toComponent: "cwi-orchestrator",
                durationMs: durationMs,
                message: context.TrustedWebContextSkippedReason,
                metadata: new Dictionary<string, object?>
                {
                    ["profile"] = context.TrustedWebContextProfile ?? _options.DefaultProfile,
                    ["error"] = ex.GetType().Name
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        return await next().ConfigureAwait(false);
    }

    private static void MarkSkipped(AskWorkflowContext context, string reason)
    {
        context.TrustedWebContextUnavailable = true;
        context.TrustedWebContextSkippedReason = reason;
        if (!context.ResponseWarnings.Contains(reason, StringComparer.OrdinalIgnoreCase))
        {
            context.ResponseWarnings.Add(reason);
        }
    }
}

public sealed class AskPromptStep : IAskWorkflowStep
{
    private readonly PromptBuilder _promptBuilder;
    private readonly BaselinePackContentProvider _baselinePackContentProvider;
    private readonly IAskHistoryRepository? _askHistoryRepository;

    public AskPromptStep(
        PromptBuilder promptBuilder,
        BaselinePackContentProvider baselinePackContentProvider,
        IAskHistoryRepository? askHistoryRepository = null)
    {
        _promptBuilder = promptBuilder;
        _baselinePackContentProvider = baselinePackContentProvider;
        _askHistoryRepository = askHistoryRepository;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (context.SkipModelInference)
        {
            return await next().ConfigureAwait(false);
        }

        if (context.BaselinePackDocuments.Count > 0 && context.BaselinePackGuidance.Count == 0)
        {
            var guidance = await _baselinePackContentProvider
                .FetchAsync(context.BaselinePackDocuments, cancellationToken)
                .ConfigureAwait(false);

            if (!guidance.IsSuccess || guidance.Data is null)
            {
                return ServiceResponse<AskResponseDto>.Fail(
                    guidance.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "Baseline pack content unavailable."),
                    statusCode: guidance.StatusCode,
                    message: guidance.Message);
            }

            context.BaselinePackGuidance = guidance.Data;
            var guidancePackNames = guidance.Data.Select(pack => pack.PackName).ToArray();
            context.CorePackExecutionOrder = CorePackOrdering.PresentCorePacks(guidancePackNames);
            context.FinalLayerExecutionOrder = CorePackOrdering.PresentFinalLayers(guidancePackNames);
        }

        var initialPromptQuestion = BuildPromptQuestion(context, Array.Empty<RecentConversationTurn>());
        var recentConversationTurns = await ResolveRecentConversationTurnsAsync(
            context,
            initialPromptQuestion,
            cancellationToken).ConfigureAwait(false);
        var promptQuestion = BuildPromptQuestion(context, recentConversationTurns);

        context.Prompt = _promptBuilder.Build(
            promptQuestion,
            context.GraphEvidence,
            context.Retrieved,
            context.OperatorProfileDecision,
            context.BaselinePackGuidance,
            context.QueryClassification,
            context.IntentType,
            context.AudienceMode,
            context.VerbosityMode,
            toneMode: context.ToneMode,
            templateUsed: context.TemplateUsed,
            evidenceRequired: context.EvidenceRequired,
            groundingMode: context.GroundingMode,
            retrievalSkippedReason: context.RetrievalSkippedReason,
            memoryResults: context.MemoryResults,
            sourceResults: context.SourceResults,
            trustedWebResults: context.TrustedWebResults,
            recentConversationTurns: recentConversationTurns);
        return await next().ConfigureAwait(false);
    }

    private static string BuildPromptQuestion(
        AskWorkflowContext context,
        IReadOnlyList<RecentConversationTurn> recentConversationTurns)
    {
        var supportingContext = AskQuestionScope.ExtractSupportingContext(context.Question);
        var primaryQuestion = context.RoutingQuestion;
        if (string.IsNullOrWhiteSpace(supportingContext)
            && ShouldUseRecentConversation(primaryQuestion)
            && recentConversationTurns.Count > 0)
        {
            primaryQuestion = BuildResolvedFollowUpQuestion(primaryQuestion, recentConversationTurns[^1]);
        }

        if (string.IsNullOrWhiteSpace(supportingContext))
        {
            return primaryQuestion;
        }

        return $"Primary question:\n{primaryQuestion}\n\n{supportingContext}";
    }

    private async Task<IReadOnlyList<RecentConversationTurn>> ResolveRecentConversationTurnsAsync(
        AskWorkflowContext context,
        string promptQuestion,
        CancellationToken cancellationToken)
    {
        if (_askHistoryRepository is null
            || HasConversationContext(promptQuestion)
            || !ShouldUseRecentConversation(context.RoutingQuestion))
        {
            return Array.Empty<RecentConversationTurn>();
        }

        var records = await _askHistoryRepository
            .ListAsync(
                context.TenantId,
                context.UserId,
                limit: 5,
                createdAfter: DateTimeOffset.UtcNow.AddDays(-2),
                cancellationToken)
            .ConfigureAwait(false);

        return records
            .Where(record =>
                string.Equals(record.ConversationId, context.ConversationId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(record.Question)
                && !string.IsNullOrWhiteSpace(record.Answer)
                && !string.Equals(record.RequestId, context.RequestId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(record => record.CreatedUtc)
            .TakeLast(3)
            .Select(record => new RecentConversationTurn(record.Question, record.Answer, record.CreatedUtc))
            .ToArray();
    }

    private static bool HasConversationContext(string promptQuestion)
    {
        return promptQuestion.Contains("Conversation context from this chat:", StringComparison.OrdinalIgnoreCase)
               || promptQuestion.Contains("Recent conversation:", StringComparison.OrdinalIgnoreCase)
               || promptQuestion.Contains("Recent chat history", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldUseRecentConversation(string question)
    {
        var normalized = $" {LanguageNormalizer.NormalizeForRouting(AskQuestionScope.ExtractPrimaryQuestion(question))} ";
        return normalized.Contains(" that ", StringComparison.Ordinal)
               || normalized.Contains(" this ", StringComparison.Ordinal)
               || normalized.Contains(" it ", StringComparison.Ordinal)
               || normalized.Contains(" them ", StringComparison.Ordinal)
               || normalized.Contains(" those ", StringComparison.Ordinal)
               || normalized.Contains(" these ", StringComparison.Ordinal)
               || normalized.Contains(" there ", StringComparison.Ordinal)
               || normalized.Contains(" nearby ", StringComparison.Ordinal)
               || normalized.Contains(" surrounding ", StringComparison.Ordinal)
               || normalized.Contains(" what about ", StringComparison.Ordinal)
               || normalized.Contains(" how about ", StringComparison.Ordinal)
               || normalized.Contains(" tell me more ", StringComparison.Ordinal)
               || normalized.Contains(" more detail", StringComparison.Ordinal);
    }

    private static string BuildResolvedFollowUpQuestion(string question, RecentConversationTurn immediatePriorTurn)
    {
        var focus = PromptBuilder.InferImmediatePriorAnswerFocus(immediatePriorTurn.Answer);
        if (string.IsNullOrWhiteSpace(focus))
        {
            return question;
        }

        var trimmedQuestion = StripConversationalFollowUpPrefix(question);
        return $"About {focus}: {trimmedQuestion}";
    }

    private static string StripConversationalFollowUpPrefix(string question)
    {
        var trimmed = question.Trim();
        var prefixes = new[]
        {
            "that's interesting.",
            "that is interesting.",
            "interesting.",
            "ok.",
            "okay.",
            "great.",
            "cool."
        };

        foreach (var prefix in prefixes)
        {
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[prefix.Length..].TrimStart();
            }
        }

        return trimmed;
    }
}

public sealed class AskInferenceStep : IAskWorkflowStep
{
    private readonly IModelClient _modelClient;
    private readonly JourneyEmitter _journeyEmitter;

    public AskInferenceStep(IModelClient modelClient, JourneyEmitter journeyEmitter)
    {
        _modelClient = modelClient;
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (context.SkipModelInference)
        {
            return await next().ConfigureAwait(false);
        }

        var prompt = context.Prompt ?? context.RoutingQuestion;
        var started = DateTimeOffset.UtcNow;
        await _journeyEmitter.EmitAsync(
            "model_inference",
            "started",
            fromComponent: "cwi-orchestrator",
            toComponent: "model_worker",
            metadata: new Dictionary<string, object?>
            {
                ["requested_model"] = context.ModelName,
                ["requested_tier"] = context.ModelTier
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var result = await _modelClient.GenerateAsync(
            new ModelRequest(prompt, context.ModelName, context.ModelTier),
            cancellationToken).ConfigureAwait(false);
        await _journeyEmitter.EmitAsync(
            "model_inference",
            result.IsSuccess && result.Data is not null ? "completed" : "failed",
            direction: "response",
            fromComponent: "model_worker",
            toComponent: "cwi-orchestrator",
            statusCode: result.StatusCode,
            durationMs: (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            message: result.Message,
            metadata: new Dictionary<string, object?>
            {
                ["model"] = result.Data?.Model
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
        {
            context.Answer = GovernedFallbackAnswerBuilder.BuildModelUnavailable(context);
            context.ModelName = "governed-fallback-response";
            context.SkipModelInference = true;
            context.FallbackApplied = true;
            AddWarning(context, "model_inference_unavailable");
            return await next().ConfigureAwait(false);
        }

        context.Answer = result.Data.Output;
        context.ModelName = result.Data.Model;
        context.Route ??= ResolveModelRoute(context);
        if (string.IsNullOrWhiteSpace(context.Answer))
        {
            context.Answer = GovernedFallbackAnswerBuilder.BuildEmptyAnswer(context);
            context.ModelName = "governed-fallback-response";
            context.SkipModelInference = true;
            context.FallbackApplied = true;
            AddWarning(context, "model_returned_empty_answer");
        }

        return await next().ConfigureAwait(false);
    }

    private static string ResolveModelRoute(AskWorkflowContext context)
    {
        if (string.Equals(context.QueryClassification, "general_model_knowledge", StringComparison.OrdinalIgnoreCase))
        {
            return "general_model_knowledge";
        }

        if (string.Equals(context.QueryClassification, "current_events", StringComparison.OrdinalIgnoreCase))
        {
            return "current_events_model_knowledge";
        }

        return AskEvidencePolicy.ShouldUseRetrievedEvidence(context)
            ? "retrieval_pack_model_answer"
            : "general_model_answer";
    }

    private static void AddWarning(AskWorkflowContext context, string warning)
    {
        if (!context.ResponseWarnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
        {
            context.ResponseWarnings.Add(warning);
        }
    }
}

public sealed class AskFallbackResponseStep : IAskWorkflowStep
{
    private readonly JourneyEmitter _journeyEmitter;

    public AskFallbackResponseStep(JourneyEmitter journeyEmitter)
    {
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(context.Answer))
        {
            return await next().ConfigureAwait(false);
        }

        context.Answer = GovernedFallbackAnswerBuilder.BuildEmptyAnswer(context);
        context.ModelName = "governed-fallback-response";
        context.SkipModelInference = true;
        context.FallbackApplied = true;
        AddWarning(context, "empty_answer_fallback_applied");

        await _journeyEmitter.EmitAsync(
            "fallback_response",
            "completed",
            fromComponent: "cwi-orchestrator",
            toComponent: "fallback_response",
            metadata: new Dictionary<string, object?>
            {
                ["intent_type"] = context.IntentType,
                ["template_used"] = context.TemplateUsed,
                ["warning"] = "empty_answer_fallback_applied"
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await next().ConfigureAwait(false);
    }

    private static void AddWarning(AskWorkflowContext context, string warning)
    {
        if (!context.ResponseWarnings.Contains(warning, StringComparer.OrdinalIgnoreCase))
        {
            context.ResponseWarnings.Add(warning);
        }
    }
}

public sealed class AskFinalCompositionStep : IAskWorkflowStep
{
    private readonly JourneyEmitter _journeyEmitter;

    public AskFinalCompositionStep(IModelClient _, JourneyEmitter journeyEmitter)
    {
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.Answer))
        {
            return await next().ConfigureAwait(false);
        }

        var finalLayers = context.BaselinePackGuidance
            .Where(IsFinalResponseLayer)
            .OrderBy(pack => CorePackOrdering.ResolveRequiredOrder(pack.PackName))
            .ToArray();
        if (finalLayers.Length == 0)
        {
            return await next().ConfigureAwait(false);
        }

        var communicationCore = finalLayers.FirstOrDefault(pack =>
            string.Equals(pack.PackName, "cwi-communication-core", StringComparison.OrdinalIgnoreCase))
            ?? finalLayers.First();
        var started = DateTimeOffset.UtcNow;
        await _journeyEmitter.EmitAsync(
            "communication_core",
            "started",
            fromComponent: "cwi-orchestrator",
            toComponent: "cwi-orchestrator",
            metadata: new Dictionary<string, object?>
            {
                ["pack_id"] = communicationCore.PackName,
                ["pack_version"] = communicationCore.PackVersion,
                ["final_layer_packs"] = finalLayers.Select(pack => $"{pack.PackName}@{pack.PackVersion}").ToArray(),
                ["hook_name"] = communicationCore.HookName,
                ["apply_stage"] = communicationCore.ApplyStage,
                ["execution_mode"] = "inline_response_behavior"
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        context.Answer = GovernedAnswerPostProcessor.ApplyCommunicationCore(context.Answer, context);
        context.CommunicationCoreApplied = true;

        await _journeyEmitter.EmitAsync(
            "communication_core",
            "completed",
            direction: "response",
            fromComponent: "cwi-orchestrator",
            toComponent: "cwi-orchestrator",
            durationMs: (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            message: "Applied communication core inline.",
            metadata: new Dictionary<string, object?>
            {
                ["pack_id"] = communicationCore.PackName,
                ["pack_version"] = communicationCore.PackVersion,
                ["final_layer_packs"] = finalLayers.Select(pack => $"{pack.PackName}@{pack.PackVersion}").ToArray(),
                ["execution_mode"] = "inline_response_behavior",
                ["tone_mode"] = context.ToneMode,
                ["template_used"] = context.TemplateUsed
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await next().ConfigureAwait(false);
    }

    private static bool IsFinalResponseLayer(BaselinePackGuidance pack)
    {
        return CorePackOrdering.IsFinalLayer(pack.PackName)
               || pack.AppliesAtStage("final_response_composition")
               || pack.HasRole("global_communication_layer");
    }
}

public sealed class AskMemoryUpdateStep : IAskWorkflowStep
{
    private readonly GovernedMemoryService _memoryService;
    private readonly JourneyEmitter _journeyEmitter;

    public AskMemoryUpdateStep(GovernedMemoryService memoryService, JourneyEmitter journeyEmitter)
    {
        _memoryService = memoryService;
        _journeyEmitter = journeyEmitter;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.TraceId))
        {
            context.TraceId = Guid.NewGuid().ToString("N");
        }

        try
        {
            context.MemoryUpdate = await _memoryService.UpdateAfterAnswerAsync(context, cancellationToken).ConfigureAwait(false);
            context.MemoryUpdated = context.MemoryUpdated
                || context.MemoryUpdate.Created.Count > 0
                || context.MemoryUpdate.Updated.Count > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.MemoryUnavailable = true;
            context.MemoryUpdate = new MemoryUpdateResult
            {
                Ignored = new[]
                {
                    new MemoryUpdateDecision
                    {
                        Reason = "memory_update_unavailable"
                    }
                },
                ProvenanceId = context.TraceId
            };
        }

        await _journeyEmitter.EmitAsync(
            "memory_capsule_update",
            "completed",
            direction: "response",
            fromComponent: "cwi-orchestrator",
            toComponent: "cwi-memory-capsule",
            metadata: new Dictionary<string, object?>
            {
                ["created_count"] = context.MemoryUpdate.Created.Count,
                ["updated_count"] = context.MemoryUpdate.Updated.Count,
                ["ignored_count"] = context.MemoryUpdate.Ignored.Count,
                ["provenance_id"] = context.MemoryUpdate.ProvenanceId
            },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return await next().ConfigureAwait(false);
    }
}

public sealed class AskSurvivalStatusStep : IAskWorkflowStep
{
    private readonly SurvivalService _survivalService;

    public AskSurvivalStatusStep(SurvivalService survivalService)
    {
        _survivalService = survivalService;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var status = await _survivalService.GetStatusAsync().ConfigureAwait(false);
        if (status.IsSuccess && status.Data is not null)
        {
            context.SurvivalMode = status.Data.Mode;
            context.SurvivalProfile = status.Data.Profile;
            context.SurvivalLastSignalUtc = status.Data.LastSignalUtc;
            context.SurvivalOfflineMessage = status.Data.OfflineMessage;
        }

        return await next().ConfigureAwait(false);
    }
}

public sealed class AskResponseStep : IAskWorkflowStep
{
    private readonly Trace.TraceLookupStore _traceStore;
    private readonly JourneyRequestContext _journeyRequestContext;
    private readonly ResponseIntelligenceOverlayService _responseIntelligenceOverlay;

    public AskResponseStep(
        Trace.TraceLookupStore traceStore,
        JourneyRequestContext journeyRequestContext,
        ResponseIntelligenceOverlayService? responseIntelligenceOverlay = null)
    {
        _traceStore = traceStore;
        _journeyRequestContext = journeyRequestContext;
        _responseIntelligenceOverlay = responseIntelligenceOverlay ?? new ResponseIntelligenceOverlayService();
    }

    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        _responseIntelligenceOverlay.Apply(context);
        var response = AskResponseFactory.Create(context, _journeyRequestContext, _traceStore);
        return Task.FromResult(ServiceResponse<AskResponseDto>.Success(response));
    }
}
#nullable disable

