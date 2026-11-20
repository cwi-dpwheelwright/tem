#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class FederatedLearningSummaryRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id_hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserIdHash { get; init; }

    [JsonPropertyName("node_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NodeId { get; init; }

    [JsonPropertyName("trace_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TraceId { get; init; }

    [JsonPropertyName("policy_status")]
    public string PolicyStatus { get; init; } = "observe";

    [JsonPropertyName("source_component")]
    public string SourceComponent { get; init; } = "local_process_miner";

    [JsonPropertyName("observations")]
    public IReadOnlyList<FederatedLearningObservationDto> Observations { get; init; } = Array.Empty<FederatedLearningObservationDto>();

    [JsonPropertyName("provenance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public FederatedLearningProvenanceDto? Provenance { get; init; }
}

public sealed class FederatedLearningObservationDto
{
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("signal")]
    public string Signal { get; init; } = string.Empty;

    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = "observed";

    [JsonPropertyName("count")]
    public int Count { get; init; } = 1;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 0.5d;

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    [JsonPropertyName("metadata")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}

public sealed class FederatedLearningProvenanceDto
{
    [JsonPropertyName("source_hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceHash { get; init; }

    [JsonPropertyName("source_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceId { get; init; }

    [JsonPropertyName("generated_utc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? GeneratedUtc { get; init; }
}

public sealed class FederatedLearningSummaryGroupDto
{
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("signal")]
    public string Signal { get; init; } = string.Empty;

    [JsonPropertyName("outcome")]
    public string Outcome { get; init; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("average_confidence")]
    public double AverageConfidence { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

public sealed class FederatedLearningSummaryResponseDto
{
    [JsonPropertyName("summary_id")]
    public string SummaryId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("approved_for_export")]
    public bool ApprovedForExport { get; init; }

    [JsonPropertyName("teacher_submission_status")]
    public string TeacherSubmissionStatus { get; init; } = "not_sent";

    [JsonPropertyName("teacher_status_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TeacherStatusCode { get; init; }

    [JsonPropertyName("raw_content_exported")]
    public bool RawContentExported { get; init; }

    [JsonPropertyName("observation_count")]
    public int ObservationCount { get; init; }

    [JsonPropertyName("summary_group_count")]
    public int SummaryGroupCount { get; init; }

    [JsonPropertyName("summary_groups")]
    public IReadOnlyList<FederatedLearningSummaryGroupDto> SummaryGroups { get; init; } = Array.Empty<FederatedLearningSummaryGroupDto>();

    [JsonPropertyName("reasons")]
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    [JsonPropertyName("trace_id")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("recorded_utc")]
    public DateTimeOffset RecordedUtc { get; init; }
}
#nullable disable
