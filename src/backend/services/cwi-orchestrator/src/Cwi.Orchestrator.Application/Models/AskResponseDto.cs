using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class AskResponseDto
{
    [JsonPropertyName("answer")]
    public string Answer { get; init; } = string.Empty;

    [JsonPropertyName("answer_markdown")]
    public string AnswerMarkdown => Answer;

    [JsonPropertyName("execution_state")]
    public string ExecutionState { get; init; } = "completed";

    [JsonPropertyName("execution_target")]
    public string ExecutionTarget { get; init; } = "shared_compute";

    [JsonPropertyName("deferred_work_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DeferredWorkId { get; init; }

    [JsonPropertyName("home_node_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HomeNodeStatus { get; init; }

    [JsonPropertyName("execution_message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExecutionMessage { get; init; }

    [JsonPropertyName("home_node_sources")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<HomeNodeSourceDto>? HomeNodeSources { get; init; }

    [JsonPropertyName("citations")]
    public IReadOnlyList<CitationDto> Citations { get; init; } = Array.Empty<CitationDto>();

    [JsonPropertyName("intent_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IntentMode { get; init; }

    [JsonPropertyName("audience_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AudienceMode { get; init; }

    [JsonPropertyName("template_used")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TemplateUsed { get; init; }

    [JsonPropertyName("verbosity_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VerbosityMode { get; init; }

    [JsonPropertyName("confidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public float? Confidence { get; init; }

    [JsonPropertyName("confidence_level")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfidenceLevel { get; init; }

    [JsonPropertyName("packs_used")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? PacksUsed { get; init; }

    [JsonPropertyName("sources")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Sources { get; init; }

    [JsonPropertyName("provenance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AskResponseProvenanceDto? Provenance { get; init; }

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Warnings { get; init; }

    [JsonPropertyName("unsupported_claims")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? UnsupportedClaims { get; init; }

    [JsonPropertyName("trace")]
    public TracePayload Trace { get; init; } = new();
}

public sealed class AskResponseProvenanceDto
{
    [JsonPropertyName("request_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestId { get; init; }

    [JsonPropertyName("trace_id")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("grounding_mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroundingMode { get; init; }

    [JsonPropertyName("model")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Model { get; init; }

    [JsonPropertyName("pack_hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PackHash { get; init; }

    [JsonPropertyName("pack_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PackVersion { get; init; }
}

public sealed class CitationDto
{
    [JsonPropertyName("doc_id")]
    public string DocId { get; init; } = string.Empty;

    [JsonPropertyName("chunk_id")]
    public string ChunkId { get; init; } = string.Empty;

    [JsonPropertyName("score")]
    public float? Score { get; init; }

    [JsonPropertyName("marker")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Marker { get; init; }

    [JsonPropertyName("source_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceId { get; init; }

    [JsonPropertyName("source_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceType { get; init; }

    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Url { get; init; }

    [JsonPropertyName("excerpt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Excerpt { get; init; }

    [JsonPropertyName("content_sha256")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentSha256 { get; init; }

    [JsonPropertyName("retrieved_utc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? RetrievedUtc { get; init; }
}

public sealed class HomeNodeSourceDto
{
    [JsonPropertyName("source_kind")]
    public string SourceKind { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonPropertyName("score")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Score { get; init; }

    [JsonPropertyName("excerpt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Excerpt { get; init; }

    [JsonPropertyName("size_bytes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SizeBytes { get; init; }
}
