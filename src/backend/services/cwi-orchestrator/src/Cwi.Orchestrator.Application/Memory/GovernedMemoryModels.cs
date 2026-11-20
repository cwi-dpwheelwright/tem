#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Memory;

public sealed class GovernedMemoryResult
{
    public string MemoryId { get; init; } = string.Empty;
    public string MemoryScope { get; init; } = "conversation";
    public string SourceType { get; init; } = "conversation_turn";
    public string Text { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string? UserId { get; init; }
    public string? NodeId { get; init; }
    public float RelevanceScore { get; init; }
    public float Confidence { get; init; }
    public string ContentSha256 { get; init; } = string.Empty;
    public string? ProvenanceId { get; init; }
}

public sealed class GovernedSourceResult
{
    public string SourceId { get; init; } = string.Empty;
    public string ChunkId { get; init; } = string.Empty;
    public string SourceType { get; init; } = "uploaded_source";
    public string Text { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string? UserId { get; init; }
    public string? NodeId { get; init; }
    public float RelevanceScore { get; init; }
    public string ContentSha256 { get; init; } = string.Empty;
    public string ExtractionMethod { get; init; } = "server_side_text_index";
}

public sealed class MemoryUpdateResult
{
    [JsonPropertyName("created")]
    public IReadOnlyList<MemoryUpdateDecision> Created { get; init; } = Array.Empty<MemoryUpdateDecision>();

    [JsonPropertyName("updated")]
    public IReadOnlyList<MemoryUpdateDecision> Updated { get; init; } = Array.Empty<MemoryUpdateDecision>();

    [JsonPropertyName("ignored")]
    public IReadOnlyList<MemoryUpdateDecision> Ignored { get; init; } = Array.Empty<MemoryUpdateDecision>();

    [JsonPropertyName("provenance_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProvenanceId { get; init; }
}

public sealed class MemoryUpdateDecision
{
    [JsonPropertyName("memory_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MemoryId { get; init; }

    [JsonPropertyName("memory_scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MemoryScope { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}

public sealed class MemoryFrameworkResult
{
    public string FrameworkVersion { get; init; } = "memory-framework-v1";
    public IReadOnlyList<GovernedMemoryResult> ActiveMemories { get; init; } = Array.Empty<GovernedMemoryResult>();
    public IReadOnlyList<MemoryFrameworkDecision> Decisions { get; init; } = Array.Empty<MemoryFrameworkDecision>();
    public IReadOnlyList<MemoryConflictSummary> Conflicts { get; init; } = Array.Empty<MemoryConflictSummary>();
}

public sealed class MemoryFrameworkDecision
{
    public string MemoryId { get; init; } = string.Empty;
    public string Slot { get; init; } = "general";
    public string Outcome { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string TemporalState { get; init; } = "current";
}

public sealed class MemoryConflictSummary
{
    public string ConflictId { get; init; } = string.Empty;
    public string Slot { get; init; } = "general";
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<string> MemoryIds { get; init; } = Array.Empty<string>();
}
#nullable disable
