using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Graph;

public sealed record GraphEvidenceRequest(
    string Question,
    string? TenantId = null,
    string? UserId = null,
    int? TopK = null);

public sealed class GraphEvidence
{
    public string Subject { get; init; } = string.Empty;

    public string Relation { get; init; } = string.Empty;

    public string Object { get; init; } = string.Empty;

    public string Template { get; init; } = string.Empty;

    public string? SourceDocument { get; init; }

    public string? SourcePackTarget { get; init; }

    public string? TargetPackTarget { get; init; }

    public bool Inferred { get; init; }

    public string Summary =>
        $"{Subject} -{Relation}-> {Object}"
        + (string.IsNullOrWhiteSpace(SourceDocument) ? string.Empty : $" (doc: {SourceDocument})")
        + (string.IsNullOrWhiteSpace(SourcePackTarget) && string.IsNullOrWhiteSpace(TargetPackTarget)
            ? string.Empty
            : $" (packs: {SourcePackTarget ?? "n/a"} -> {TargetPackTarget ?? "n/a"})")
        + (Inferred ? " [inferred]" : " [direct]");

    public string SourceToken =>
        string.IsNullOrWhiteSpace(SourceDocument)
            ? $"graph:{Template}:{Subject}:{Relation}:{Object}"
            : $"graph:{Template}:{SourceDocument}";
}

public sealed class GraphQueryResult
{
    public string? Template { get; init; }

    public IReadOnlyList<string> Terms { get; init; } = Array.Empty<string>();

    public IReadOnlyList<GraphEvidence> Evidence { get; init; } = Array.Empty<GraphEvidence>();
}

public sealed class GraphQueryPlan
{
    public required string Template { get; init; }

    public required string Query { get; init; }

    public IReadOnlyList<string> Terms { get; init; } = Array.Empty<string>();
}

public sealed class ReasoningReceipt
{
    [JsonPropertyName("grounded")]
    public bool Grounded { get; init; }

    [JsonPropertyName("grounding_mode")]
    public string GroundingMode { get; init; } = string.Empty;

    [JsonPropertyName("retrieval_allowed")]
    public bool RetrievalAllowed { get; init; }

    [JsonPropertyName("retrieval_skipped_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RetrievalSkippedReason { get; init; }

    [JsonPropertyName("confidence_basis")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfidenceBasis { get; init; }

    [JsonPropertyName("graph_used")]
    public bool GraphUsed { get; init; }

    [JsonPropertyName("graph_query_template")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GraphQueryTemplate { get; init; }

    [JsonPropertyName("graph_terms")]
    public IReadOnlyList<string> GraphTerms { get; init; } = Array.Empty<string>();

    [JsonPropertyName("graph_fallback_used")]
    public bool GraphFallbackUsed { get; init; }

    [JsonPropertyName("graph_failure_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GraphFailureReason { get; init; }

    [JsonPropertyName("graph_evidence_count")]
    public int GraphEvidenceCount { get; init; }

    [JsonPropertyName("document_evidence_count")]
    public int DocumentEvidenceCount { get; init; }
}
