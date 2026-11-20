using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Memory;
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class TracePayload
{
    [JsonPropertyName("request_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestId { get; init; }

    [JsonPropertyName("trace_id")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("sources")]
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();

    [JsonPropertyName("confidence")]
    public float Confidence { get; init; }

    [JsonPropertyName("confidence_level")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfidenceLevel { get; init; }

    [JsonPropertyName("confidence_basis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfidenceBasis { get; init; }

    [JsonPropertyName("raw_prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RawPrompt { get; init; }

    [JsonPropertyName("normalized_prompt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NormalizedPrompt { get; init; }

    [JsonPropertyName("normalization_applied")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NormalizationApplied { get; init; }

    [JsonPropertyName("normalization_rules")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? NormalizationRules { get; init; }

    [JsonPropertyName("normalization_failed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NormalizationFailed { get; init; }

    [JsonPropertyName("normalization_failure_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NormalizationFailureReason { get; init; }

    [JsonPropertyName("normalization_confidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? NormalizationConfidence { get; init; }

    [JsonPropertyName("normalization_confidence_threshold")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? NormalizationConfidenceThreshold { get; init; }

    [JsonPropertyName("normalization_requires_verification")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? NormalizationRequiresVerification { get; init; }

    [JsonPropertyName("normalization_verification_message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NormalizationVerificationMessage { get; init; }

    [JsonPropertyName("normalization_verification_candidates")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? NormalizationVerificationCandidates { get; init; }

    [JsonPropertyName("normalization_correction_decision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NormalizationCorrectionDecision { get; init; }

    [JsonPropertyName("normalization_correction_risk")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NormalizationCorrectionRisk { get; init; }

    [JsonPropertyName("normalization_correction_visibility")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NormalizationCorrectionVisibility { get; init; }

    [JsonPropertyName("normalization_corrections")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<NormalizationCorrectionTrace>? NormalizationCorrections { get; init; }

    [JsonPropertyName("grounding_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroundingMode { get; init; }

    [JsonPropertyName("grounded")]
    public bool Grounded { get; init; }

    [JsonPropertyName("reasoning_based")]
    public bool ReasoningBased { get; init; }

    [JsonPropertyName("route")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Route { get; init; }

    [JsonPropertyName("entity_found")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EntityFound { get; init; }

    [JsonPropertyName("pack_used")]
    public bool PackUsed { get; init; }

    [JsonPropertyName("memory_used")]
    public bool MemoryUsed { get; init; }

    [JsonPropertyName("graph_used")]
    public bool GraphUsed { get; init; }

    [JsonPropertyName("retrieval_allowed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? RetrievalAllowed { get; init; }

    [JsonPropertyName("retrieval_skipped_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RetrievalSkippedReason { get; init; }

    [JsonPropertyName("retrieval_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RetrievalStatus { get; init; }

    [JsonPropertyName("retrieval_domain")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RetrievalDomain { get; init; }

    [JsonPropertyName("retrieval_domain_terms")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? RetrievalDomainTerms { get; init; }

    [JsonPropertyName("graph_hits")]
    public int GraphHits { get; init; }

    [JsonPropertyName("document_hits")]
    public int DocumentHits { get; init; }

    [JsonPropertyName("memory_results")]
    public IReadOnlyList<MemoryTraceItem> MemoryResults { get; init; } = Array.Empty<MemoryTraceItem>();

    [JsonPropertyName("memory_framework_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MemoryFrameworkVersion { get; init; }

    [JsonPropertyName("memory_active_context_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MemoryActiveContextCount { get; init; }

    [JsonPropertyName("memory_excluded_context_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MemoryExcludedContextCount { get; init; }

    [JsonPropertyName("memory_framework_decisions")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MemoryFrameworkTraceDecision>? MemoryFrameworkDecisions { get; init; }

    [JsonPropertyName("memory_conflicts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MemoryConflictTraceItem>? MemoryConflicts { get; init; }

    [JsonPropertyName("source_results")]
    public IReadOnlyList<SourceTraceItem> SourceResults { get; init; } = Array.Empty<SourceTraceItem>();

    [JsonPropertyName("memory_update")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MemoryUpdateResult? MemoryUpdate { get; init; }

    [JsonPropertyName("memory_unavailable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MemoryUnavailable { get; init; }

    [JsonPropertyName("source_retrieval_unavailable")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SourceRetrievalUnavailable { get; init; }

    [JsonPropertyName("trusted_web_requested")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TrustedWebRequested { get; init; }

    [JsonPropertyName("trusted_web_enabled")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TrustedWebEnabled { get; init; }

    [JsonPropertyName("trusted_web_profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TrustedWebProfile { get; init; }

    [JsonPropertyName("trusted_web_hits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TrustedWebHits { get; init; }

    [JsonPropertyName("trusted_web_skipped_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TrustedWebSkippedReason { get; init; }

    [JsonPropertyName("trusted_web_retrieval_duration_ms")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TrustedWebRetrievalDurationMs { get; init; }

    [JsonPropertyName("trusted_web_sources")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TrustedWebTraceSource>? TrustedWebSources { get; init; }

    [JsonPropertyName("policy_mode")]
    public string PolicyMode { get; init; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("pack_hash")]
    public string PackHash { get; init; } = string.Empty;

    [JsonPropertyName("pack_version")]
    public string PackVersion { get; init; } = string.Empty;

    [JsonPropertyName("pack_hashes")]
    public IReadOnlyDictionary<string, string> PackHashes { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("pack_capabilities")]
    public IReadOnlyList<PackCapabilitySummary> PackCapabilities { get; init; } = Array.Empty<PackCapabilitySummary>();

    [JsonPropertyName("pack_capability_decision")]
    public PackCapabilityDecisionSummary? PackCapabilityDecision { get; init; }

    [JsonPropertyName("baseline_packs")]
    public IReadOnlyList<BaselinePackTraceItem> BaselinePacks { get; init; } = Array.Empty<BaselinePackTraceItem>();

    [JsonPropertyName("core_pack_order")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? CorePackOrder { get; init; }

    [JsonPropertyName("domain_packs")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? DomainPacks { get; init; }

    [JsonPropertyName("final_layers")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? FinalLayers { get; init; }

    [JsonPropertyName("query_classification")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QueryClassification { get; init; }

    [JsonPropertyName("query_classification_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? QueryClassificationReason { get; init; }

    [JsonPropertyName("intent_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntentType { get; init; }

    [JsonPropertyName("audience_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AudienceMode { get; init; }

    [JsonPropertyName("verbosity_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerbosityMode { get; init; }

    [JsonPropertyName("intent_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntentMode { get; init; }

    [JsonPropertyName("tone_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToneMode { get; init; }

    [JsonPropertyName("comprehension_expression_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ComprehensionExpressionVersion { get; init; }

    [JsonPropertyName("context_awareness_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContextAwarenessMode { get; init; }

    [JsonPropertyName("clarity_decision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClarityDecision { get; init; }

    [JsonPropertyName("clarity_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClarityReason { get; init; }

    [JsonPropertyName("expression_tone_profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpressionToneProfile { get; init; }

    [JsonPropertyName("expression_tone_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExpressionToneReason { get; init; }

    [JsonPropertyName("template_used")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplateUsed { get; init; }

    [JsonPropertyName("presentation_framework_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PresentationFrameworkVersion { get; init; }

    [JsonPropertyName("presentation_template_family")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PresentationTemplateFamily { get; init; }

    [JsonPropertyName("presentation_template_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PresentationTemplateReason { get; init; }

    [JsonPropertyName("presentation_rhythm_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PresentationRhythmMode { get; init; }

    [JsonPropertyName("presentation_rhythm_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PresentationRhythmReason { get; init; }

    [JsonPropertyName("presentation_formatting_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PresentationFormattingMode { get; init; }

    [JsonPropertyName("presentation_formatting_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PresentationFormattingReason { get; init; }

    [JsonPropertyName("presentation_source_display_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PresentationSourceDisplayMode { get; init; }

    [JsonPropertyName("presentation_source_display_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PresentationSourceDisplayReason { get; init; }

    [JsonPropertyName("chat_response_quality_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChatResponseQualityVersion { get; init; }

    [JsonPropertyName("chat_response_profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChatResponseProfile { get; init; }

    [JsonPropertyName("chat_response_profile_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChatResponseProfileReason { get; init; }

    [JsonPropertyName("chat_context_budget_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChatContextBudgetMode { get; init; }

    [JsonPropertyName("chat_context_budget_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ChatContextBudgetReason { get; init; }

    [JsonPropertyName("chat_final_polish_applied")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? ChatFinalPolishApplied { get; init; }

    [JsonPropertyName("mcp_tool")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? McpTool { get; init; }

    [JsonPropertyName("mcp_execution_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? McpExecutionStatus { get; init; }

    [JsonPropertyName("mcp_policy_decision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? McpPolicyDecision { get; init; }

    [JsonPropertyName("communication_runtime_applied")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CommunicationRuntimeApplied { get; init; }

    [JsonPropertyName("evidence_required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? EvidenceRequired { get; init; }

    [JsonPropertyName("truth_engine_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TruthEngineVersion { get; init; }

    [JsonPropertyName("truth_engine_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TruthEngineMode { get; init; }

    [JsonPropertyName("truth_state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TruthState { get; init; }

    [JsonPropertyName("truth_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TruthReason { get; init; }

    [JsonPropertyName("truth_evidence_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TruthEvidenceCount { get; init; }

    [JsonPropertyName("truth_conflict_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TruthConflictCount { get; init; }

    [JsonPropertyName("truth_evidence_kinds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? TruthEvidenceKinds { get; init; }

    [JsonPropertyName("truth_evaluated_utc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? TruthEvaluatedUtc { get; init; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Warnings { get; init; }

    [JsonPropertyName("memory_updated")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MemoryUpdated { get; init; }

    [JsonPropertyName("applied_preference_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppliedPreferenceMode { get; init; }

    [JsonPropertyName("governed_source_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? GovernedSourceCount { get; init; }

    [JsonPropertyName("communication_core_applied")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? CommunicationCoreApplied { get; init; }

    [JsonPropertyName("communication_core_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CommunicationCoreVersion { get; init; }

    [JsonPropertyName("communication_core_hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CommunicationCoreHash { get; init; }

    [JsonPropertyName("final_answer_hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinalAnswerHash { get; init; }

    [JsonPropertyName("guardrails")]
    public IReadOnlyList<GuardrailViolation> Guardrails { get; init; } = Array.Empty<GuardrailViolation>();

    [JsonPropertyName("guardrails_summary")]
    public GuardrailSummary? GuardrailsSummary { get; init; }

    [JsonPropertyName("guardrails_pointers")]
    public IReadOnlyList<string> GuardrailsPointers { get; init; } = Array.Empty<string>();

    [JsonPropertyName("validations")]
    public IReadOnlyList<ValidationIssue> Validations { get; init; } = Array.Empty<ValidationIssue>();

    [JsonPropertyName("survival_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SurvivalMode { get; init; }

    [JsonPropertyName("survival_profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SurvivalProfile { get; init; }

    [JsonPropertyName("survival_last_signal_utc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? SurvivalLastSignalUtc { get; init; }

    [JsonPropertyName("discrepancy_severity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DiscrepancySeverity { get; init; }

    [JsonPropertyName("discrepancy_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DiscrepancyReason { get; init; }

    [JsonPropertyName("discrepancy_score")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? DiscrepancyScore { get; init; }

    [JsonPropertyName("discrepancy_requires_approval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DiscrepancyRequiresApproval { get; init; }

    [JsonPropertyName("discrepancy_ticket_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DiscrepancyTicketId { get; init; }

    [JsonPropertyName("fallback_applied")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? FallbackApplied { get; init; }

    [JsonPropertyName("reasoning_receipt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReasoningReceipt? ReasoningReceipt { get; init; }

    [JsonPropertyName("discrepancies")]
    public IReadOnlyList<DiscrepancyRecord> Discrepancies { get; init; } = Array.Empty<DiscrepancyRecord>();

    [JsonPropertyName("corrections")]
    public IReadOnlyList<CorrectionDiff> Corrections { get; init; } = Array.Empty<CorrectionDiff>();

    [JsonPropertyName("operator_profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OperatorProfileSummaryDto? OperatorProfile { get; init; }

    [JsonPropertyName("operator_profile_decision")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OperatorProfileDecisionDto? OperatorProfileDecision { get; init; }

    [JsonPropertyName("execution_state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutionState { get; init; }

    [JsonPropertyName("execution_target")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutionTarget { get; init; }

    [JsonPropertyName("deferred_work_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeferredWorkId { get; init; }

    [JsonPropertyName("home_node_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeNodeId { get; init; }

    [JsonPropertyName("home_node_owner_user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeNodeOwnerUserId { get; init; }

    [JsonPropertyName("home_node_tailscale_ip")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeNodeTailscaleIp { get; init; }

    [JsonPropertyName("home_node_membership_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeNodeMembershipStatus { get; init; }

    [JsonPropertyName("home_node_membership_last_seen_utc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? HomeNodeMembershipLastSeenUtc { get; init; }

    [JsonPropertyName("home_node_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeNodeStatus { get; init; }

    [JsonPropertyName("home_node_source_count")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? HomeNodeSourceCount { get; init; }

    [JsonPropertyName("home_node_execution_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeNodeExecutionMode { get; init; }

    [JsonPropertyName("home_node_local_policy_outcome")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeNodeLocalPolicyOutcome { get; init; }

    [JsonPropertyName("home_node_bundle_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeNodeBundleId { get; init; }

    [JsonPropertyName("home_node_pack_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeNodePackId { get; init; }

    [JsonPropertyName("home_node_pack_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeNodePackName { get; init; }

    [JsonPropertyName("home_node_pack_anchor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeNodePackAnchor { get; init; }

    [JsonPropertyName("home_node_search_terms")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? HomeNodeSearchTerms { get; init; }
}

public sealed class BaselinePackTraceItem
{
    [JsonPropertyName("pack_id")]
    public string PackId { get; init; } = string.Empty;

    [JsonPropertyName("pack_version")]
    public string PackVersion { get; init; } = string.Empty;

    [JsonPropertyName("pack_hash")]
    public string PackHash { get; init; } = string.Empty;
}

public sealed class NormalizationCorrectionTrace
{
    [JsonPropertyName("original")]
    public string Original { get; init; } = string.Empty;

    [JsonPropertyName("normalized")]
    public string Normalized { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("visibility")]
    public string Visibility { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

public sealed class MemoryTraceItem
{
    [JsonPropertyName("memory_id")]
    public string MemoryId { get; init; } = string.Empty;

    [JsonPropertyName("memory_scope")]
    public string MemoryScope { get; init; } = string.Empty;

    [JsonPropertyName("source_type")]
    public string SourceType { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    [JsonPropertyName("node_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NodeId { get; init; }

    [JsonPropertyName("relevance_score")]
    public float RelevanceScore { get; init; }

    [JsonPropertyName("confidence")]
    public float Confidence { get; init; }

    [JsonPropertyName("content_sha256")]
    public string ContentSha256 { get; init; } = string.Empty;

    [JsonPropertyName("provenance_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProvenanceId { get; init; }
}

public sealed class MemoryFrameworkTraceDecision
{
    [JsonPropertyName("memory_id")]
    public string MemoryId { get; init; } = string.Empty;

    [JsonPropertyName("slot")]
    public string Slot { get; init; } = string.Empty;

    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("temporal_state")]
    public string TemporalState { get; init; } = string.Empty;
}

public sealed class MemoryConflictTraceItem
{
    [JsonPropertyName("conflict_id")]
    public string ConflictId { get; init; } = string.Empty;

    [JsonPropertyName("slot")]
    public string Slot { get; init; } = string.Empty;

    [JsonPropertyName("memory_ids")]
    public IReadOnlyList<string> MemoryIds { get; init; } = Array.Empty<string>();
}

public sealed class SourceTraceItem
{
    [JsonPropertyName("source_id")]
    public string SourceId { get; init; } = string.Empty;

    [JsonPropertyName("chunk_id")]
    public string ChunkId { get; init; } = string.Empty;

    [JsonPropertyName("source_type")]
    public string SourceType { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    [JsonPropertyName("node_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NodeId { get; init; }

    [JsonPropertyName("relevance_score")]
    public float RelevanceScore { get; init; }

    [JsonPropertyName("content_sha256")]
    public string ContentSha256 { get; init; } = string.Empty;

    [JsonPropertyName("extraction_method")]
    public string ExtractionMethod { get; init; } = string.Empty;
}

public sealed class TrustedWebTraceSource
{
    [JsonPropertyName("source_id")]
    public string SourceId { get; init; } = string.Empty;

    [JsonPropertyName("evidence_source_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EvidenceSourceId { get; init; }

    [JsonPropertyName("marker")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Marker { get; init; }

    [JsonPropertyName("source_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceType { get; init; }

    [JsonPropertyName("trust_tier")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TrustTier { get; init; }

    [JsonPropertyName("publisher")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Publisher { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("canonical_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CanonicalUrl { get; init; }

    [JsonPropertyName("chunk_id")]
    public string ChunkId { get; init; } = string.Empty;

    [JsonPropertyName("excerpt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Excerpt { get; init; }

    [JsonPropertyName("content_sha256")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentSha256 { get; init; }

    [JsonPropertyName("score")]
    public float Score { get; init; }

    [JsonPropertyName("retrieved_utc")]
    public DateTimeOffset RetrievedUtc { get; init; }
}

public sealed class GuardrailViolation
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("severity")]
    public string? Severity { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

public sealed class GuardrailSummary
{
    [JsonPropertyName("total")]
    public int Total { get; init; }

    [JsonPropertyName("severity_counts")]
    public IReadOnlyDictionary<string, int> SeverityCounts { get; init; } = new Dictionary<string, int>();
}

public sealed class ValidationIssue
{
    [JsonPropertyName("rule")]
    public string? Rule { get; init; }

    [JsonPropertyName("field")]
    public string? Field { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}

public sealed class DiscrepancyRecord
{
    [JsonPropertyName("field")]
    public string? Field { get; init; }

    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; init; }

    [JsonPropertyName("expected")]
    public string? Expected { get; init; }

    [JsonPropertyName("actual")]
    public string? Actual { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

public sealed class CorrectionDiff
{
    [JsonPropertyName("field")]
    public string? Field { get; init; }

    [JsonPropertyName("from")]
    public string? From { get; init; }

    [JsonPropertyName("to")]
    public string? To { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

public sealed class PackCapabilitySummary
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }

    [JsonPropertyName("risk")]
    public string? Risk { get; init; }

    [JsonPropertyName("requires_approval")]
    public bool RequiresApproval { get; init; }
}

public sealed class PackCapabilityDecisionSummary
{
    [JsonPropertyName("policy_mode")]
    public string PolicyMode { get; init; } = string.Empty;

    [JsonPropertyName("risk")]
    public string? Risk { get; init; }

    [JsonPropertyName("requires_approval")]
    public bool RequiresApproval { get; init; }

    [JsonPropertyName("allow_execution")]
    public bool AllowExecution { get; init; }

    [JsonPropertyName("blocking_capabilities")]
    public IReadOnlyList<string> BlockingCapabilities { get; init; } = Array.Empty<string>();

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
