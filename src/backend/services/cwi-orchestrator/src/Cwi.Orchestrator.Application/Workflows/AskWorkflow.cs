#nullable enable
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Application.Retrieval;
using Cwi.Orchestrator.Application.TrustedWeb;

namespace Cwi.Orchestrator.Application.Workflows;

public sealed class AskWorkflowContext
{
    public const float NormalizationContinuationConfidenceThreshold = 0.85f;

    public AskWorkflowContext(
        string tenantId,
        string userId,
        string question,
        string packName,
        string? requestedPackAnchor,
        string policyMode,
        int topK,
        string? conversationId = null,
        string? effectiveTimezone = null,
        bool useWebRequested = false,
        LanguageNormalizationResult? normalization = null)
    {
        TenantId = tenantId;
        UserId = userId;
        Question = question;
        var resolvedNormalization = normalization ?? LanguageNormalizer.Normalize(question);
        RawQuestion = question;
        NormalizedQuestion = resolvedNormalization.NormalizedPrompt;
        NormalizationApplied = resolvedNormalization.Applied;
        NormalizationRules = resolvedNormalization.AppliedRules;
        NormalizationFailed = resolvedNormalization.Failed;
        NormalizationFailureReason = resolvedNormalization.FailureReason;
        NormalizationConfidence = ClampConfidence(resolvedNormalization.Confidence);
        NormalizationConfidenceThreshold = NormalizationContinuationConfidenceThreshold;
        NormalizationRequiresVerification = !NormalizationFailed
            && (resolvedNormalization.RequiresVerification
                || NormalizationConfidence < NormalizationConfidenceThreshold);
        NormalizationVerificationMessage = resolvedNormalization.VerificationMessage;
        NormalizationVerificationCandidates = resolvedNormalization.VerificationCandidates ?? Array.Empty<string>();
        NormalizationCorrections = resolvedNormalization.Corrections ?? Array.Empty<LanguageCorrection>();
        NormalizationCorrectionDecision = resolvedNormalization.CorrectionDecision;
        NormalizationCorrectionRisk = resolvedNormalization.CorrectionRisk;
        NormalizationCorrectionVisibility = resolvedNormalization.CorrectionVisibility;
        PackName = packName;
        RequestedPackAnchor = requestedPackAnchor;
        PolicyMode = policyMode;
        TopK = topK;
        ConversationId = string.IsNullOrWhiteSpace(conversationId) ? "default" : conversationId.Trim();
        EffectiveTimezone = string.IsNullOrWhiteSpace(effectiveTimezone) ? null : effectiveTimezone.Trim();
        UseWebRequested = useWebRequested;
        DomainPackNames = new[] { packName };
    }

    public string TenantId { get; }
    public string UserId { get; }
    public string Question { get; }
    public string RawQuestion { get; }
    public string NormalizedQuestion { get; }
    public string RoutingQuestion => string.IsNullOrWhiteSpace(NormalizedQuestion) ? Question : NormalizedQuestion;
    public bool NormalizationApplied { get; }
    public IReadOnlyList<string> NormalizationRules { get; }
    public bool NormalizationFailed { get; }
    public string? NormalizationFailureReason { get; }
    public float NormalizationConfidence { get; }
    public float NormalizationConfidenceThreshold { get; }
    public bool NormalizationRequiresVerification { get; }
    public string? NormalizationVerificationMessage { get; }
    public IReadOnlyList<string> NormalizationVerificationCandidates { get; }
    public IReadOnlyList<LanguageCorrection> NormalizationCorrections { get; }
    public string NormalizationCorrectionDecision { get; }
    public string NormalizationCorrectionRisk { get; }
    public string NormalizationCorrectionVisibility { get; }
    public string PackName { get; }
    public string? RequestedPackAnchor { get; }
    public string PolicyMode { get; set; }
    public int TopK { get; }
    public string ConversationId { get; }
    public string? EffectiveTimezone { get; }
    public bool UseWebRequested { get; }

    public PackDocument? PackDocument { get; set; }
    public PackManifestMetadata? PackMetadata { get; set; }
    public string? PackHash { get; set; }
    public string? PackVersion { get; set; }
    public IReadOnlyList<PackDocument> BaselinePackDocuments { get; set; } = Array.Empty<PackDocument>();
    public IReadOnlyDictionary<string, PackManifestMetadata> BaselinePackMetadata { get; set; } = new Dictionary<string, PackManifestMetadata>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<BaselinePackGuidance> BaselinePackGuidance { get; set; } = Array.Empty<BaselinePackGuidance>();
    public IReadOnlyList<string> CorePackExecutionOrder { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> DomainPackNames { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> FinalLayerExecutionOrder { get; set; } = Array.Empty<string>();
    public string? QueryClassification { get; set; }
    public string? QueryClassificationReason { get; set; }
    public string? IntentType { get; set; }
    public string? AudienceMode { get; set; }
    public string? VerbosityMode { get; set; }
    public string? ToneMode { get; set; }
    public string? TemplateUsed { get; set; }
    public bool CommunicationRuntimeApplied { get; set; }
    public string? ComprehensionExpressionVersion { get; set; }
    public string? ContextAwarenessMode { get; set; }
    public string? ClarityDecision { get; set; }
    public string? ClarityReason { get; set; }
    public string? ExpressionToneProfile { get; set; }
    public string? ExpressionToneReason { get; set; }
    public string? PresentationFrameworkVersion { get; set; }
    public string? PresentationTemplateFamily { get; set; }
    public string? PresentationTemplateReason { get; set; }
    public string? PresentationRhythmMode { get; set; }
    public string? PresentationRhythmReason { get; set; }
    public string? PresentationFormattingMode { get; set; }
    public string? PresentationFormattingReason { get; set; }
    public string? PresentationSourceDisplayMode { get; set; }
    public string? PresentationSourceDisplayReason { get; set; }
    public string? ChatResponseQualityVersion { get; set; }
    public string? ChatResponseProfile { get; set; }
    public string? ChatResponseProfileReason { get; set; }
    public string? ChatContextBudgetMode { get; set; }
    public string? ChatContextBudgetReason { get; set; }
    public bool? ChatFinalPolishApplied { get; set; }
    public bool RetrievalAllowed { get; set; } = true;
    public string? RetrievalSkippedReason { get; set; }
    public string? RetrievalDomain { get; set; }
    public IReadOnlyList<string> RetrievalDomainTerms { get; set; } = Array.Empty<string>();
    public float RetrievalMinScore { get; set; } = 0.35f;
    public bool EvidenceRequired { get; set; }
    public string GroundingMode { get; set; } = "source_grounded";
    public string? ConfidenceBasis { get; set; }
    public string? TruthEngineVersion { get; set; }
    public string? TruthEngineMode { get; set; }
    public string? TruthState { get; set; }
    public string? TruthReason { get; set; }
    public int? TruthEvidenceCount { get; set; }
    public int? TruthConflictCount { get; set; }
    public IReadOnlyList<string> TruthEvidenceKinds { get; set; } = Array.Empty<string>();
    public DateTimeOffset? TruthEvaluatedUtc { get; set; }
    public string? MemoryUpdateKind { get; set; }
    public string? MemoryUpdateValue { get; set; }
    public bool MemoryUpdated { get; set; }
    public bool SkipModelInference { get; set; }
    public string? AppliedPreferenceMode { get; set; }
    public List<GraphEvidence> GraphEvidence { get; } = new();
    public string? GraphQueryTemplate { get; set; }
    public IReadOnlyList<string> GraphQueryTerms { get; set; } = Array.Empty<string>();
    public bool GraphUnavailable { get; set; }
    public string? GraphFailureReason { get; set; }
    public IReadOnlyList<GovernedMemoryResult> MemoryResults { get; set; } = Array.Empty<GovernedMemoryResult>();
    public string? MemoryFrameworkVersion { get; set; }
    public IReadOnlyList<MemoryFrameworkDecision> MemoryFrameworkDecisions { get; set; } = Array.Empty<MemoryFrameworkDecision>();
    public IReadOnlyList<MemoryConflictSummary> MemoryConflictSummaries { get; set; } = Array.Empty<MemoryConflictSummary>();
    public int MemoryActiveContextCount { get; set; }
    public int MemoryExcludedContextCount { get; set; }
    public IReadOnlyList<GovernedSourceResult> SourceResults { get; set; } = Array.Empty<GovernedSourceResult>();
    public bool TrustedWebContextRequested { get; set; }
    public bool TrustedWebContextEnabled { get; set; }
    public string? TrustedWebContextMode { get; set; }
    public string? TrustedWebContextProfile { get; set; }
    public int? TrustedWebContextMaxResults { get; set; }
    public IReadOnlyList<TrustedWebResult> TrustedWebResults { get; set; } = Array.Empty<TrustedWebResult>();
    public bool TrustedWebContextUnavailable { get; set; }
    public string? TrustedWebContextSkippedReason { get; set; }
    public long? TrustedWebContextRetrievalDurationMs { get; set; }
    public MemoryUpdateResult? MemoryUpdate { get; set; }
    public bool MemoryUnavailable { get; set; }
    public bool SourceRetrievalUnavailable { get; set; }
    public List<RetrievalResult> Retrieved { get; } = new();
    public string? Prompt { get; set; }
    public string? Answer { get; set; }
    public string? RequestId { get; set; }
    public string? TraceId { get; set; }
    public string? ModelName { get; set; }
    public string? ModelTier { get; set; }
    public bool CommunicationCoreApplied { get; set; }
    public string? FinalAnswerHash { get; set; }
    public bool RetrievalUnavailable { get; set; }
    public bool? Grounded { get; set; }
    public bool? ReasoningBased { get; set; }
    public string? Route { get; set; }
    public bool? EntityFound { get; set; }
    public IReadOnlyList<GuardrailViolation> GuardrailViolations { get; set; } = Array.Empty<GuardrailViolation>();
    public Guardrails.Diagnostics.GuardrailDiagnostics? GuardrailDiagnostics { get; set; }
    public string? PolicyVersion { get; set; }
    public Packs.PackCapabilityMap? PackCapabilities { get; set; }
    public Packs.Policy.PackCapabilityDecision? PackCapabilityDecision { get; set; }
    public IReadOnlyList<DiscrepancyRecord> Discrepancies { get; set; } = Array.Empty<DiscrepancyRecord>();
    public IReadOnlyList<CorrectionDiff> Corrections { get; set; } = Array.Empty<CorrectionDiff>();
    public string? DiscrepancySeverity { get; set; }
    public string? DiscrepancyReason { get; set; }
    public double? DiscrepancyScore { get; set; }
    public bool FallbackApplied { get; set; }
    public string? DiscrepancyTicketId { get; set; }
    public bool DiscrepancyRequiresApproval { get; set; }
    public string? SurvivalMode { get; set; }
    public string? SurvivalProfile { get; set; }
    public List<string> ResponseWarnings { get; } = new();
    public DateTimeOffset? SurvivalLastSignalUtc { get; set; }
    public string? SurvivalOfflineMessage { get; set; }
    public OperatorProfileSummaryDto? OperatorProfile { get; set; }
    public OperatorProfileDecisionDto? OperatorProfileDecision { get; set; }
    public string ExecutionState { get; set; } = "completed";
    public string ExecutionTarget { get; set; } = "shared_compute";
    public string? DeferredWorkId { get; set; }
    public string? HomeNodeId { get; set; }
    public string? HomeNodeOwnerUserId { get; set; }
    public string? HomeNodeTailscaleIp { get; set; }
    public string? HomeNodeMembershipStatus { get; set; }
    public DateTimeOffset? HomeNodeMembershipLastSeenUtc { get; set; }
    public string? HomeNodeStatus { get; set; }
    public string? ExecutionMessage { get; set; }
    public IReadOnlyList<HomeNodeSourceDto> HomeNodeSources { get; set; } = Array.Empty<HomeNodeSourceDto>();
    public string? HomeNodeExecutionMode { get; set; }
    public string? HomeNodeLocalPolicyOutcome { get; set; }
    public string? HomeNodeBundleId { get; set; }
    public string? HomeNodePackId { get; set; }
    public string? HomeNodePackName { get; set; }
    public string? HomeNodePackAnchor { get; set; }
    public IReadOnlyList<string> HomeNodeSearchTerms { get; set; } = Array.Empty<string>();
    public Mcp.McpToolDefinition? McpTool { get; set; }
    public Mcp.McpPolicyEvaluation? McpPolicy { get; set; }
    public Dictionary<string, object?>? McpResult { get; set; }
    public bool McpExecuted { get; set; }
    public string? McpExecutionStatus { get; set; }
    public string? McpExecutionToolName { get; set; }
    public string? McpPolicyDecision { get; set; }

    private static float ClampConfidence(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0.0f;
        }

        return Math.Max(0.0f, Math.Min(1.0f, value));
    }
}

public interface IAskWorkflowStep : IWorkflowStep<AskWorkflowContext, AskResponseDto>
{
}

public interface IAskWorkflowDecorator : IWorkflowDecorator<AskWorkflowContext, AskResponseDto>
{
}

public sealed class AskWorkflowPipeline : WorkflowPipeline<AskWorkflowContext, AskResponseDto>
{
    public AskWorkflowPipeline(
        IEnumerable<IAskWorkflowStep> steps,
        IEnumerable<IAskWorkflowDecorator> decorators)
        : base(steps, decorators)
    {
    }
}
#nullable disable
