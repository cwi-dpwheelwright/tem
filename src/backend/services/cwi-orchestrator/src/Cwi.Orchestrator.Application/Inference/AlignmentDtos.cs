#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Inference;

public sealed class MultiSourceAlignmentDto
{
    [JsonPropertyName("inputs")]
    public IReadOnlyList<AlignmentInputDto> Inputs { get; init; } = Array.Empty<AlignmentInputDto>();

    [JsonPropertyName("normalized")]
    public IReadOnlyList<NormalizedFieldDto> Normalized { get; init; } = Array.Empty<NormalizedFieldDto>();

    [JsonPropertyName("alignment_map")]
    public IReadOnlyList<AlignmentMapEntryDto> AlignmentMap { get; init; } = Array.Empty<AlignmentMapEntryDto>();

    [JsonPropertyName("ambiguities")]
    public IReadOnlyList<AlignmentAmbiguityDto> Ambiguities { get; init; } = Array.Empty<AlignmentAmbiguityDto>();
}

public sealed class AlignmentInputDto
{
    [JsonPropertyName("source_id")]
    public string SourceId { get; init; } = string.Empty;

    [JsonPropertyName("model_id")]
    public string? ModelId { get; init; }

    [JsonPropertyName("timestamp_utc")]
    public DateTimeOffset? TimestampUtc { get; init; }

    [JsonPropertyName("raw_output")]
    public string RawOutput { get; init; } = string.Empty;
}

public sealed class NormalizedFieldDto
{
    [JsonPropertyName("field")]
    public string Field { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public double Value { get; init; }

    [JsonPropertyName("unit")]
    public string Unit { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 1.0;
}

public sealed class AlignmentMapEntryDto
{
    [JsonPropertyName("source_id")]
    public string SourceId { get; init; } = string.Empty;

    [JsonPropertyName("field")]
    public string Field { get; init; } = string.Empty;

    [JsonPropertyName("normalized_field")]
    public string NormalizedField { get; init; } = string.Empty;
}

public sealed class AlignmentAmbiguityDto
{
    [JsonPropertyName("field")]
    public string Field { get; init; } = string.Empty;

    [JsonPropertyName("candidates")]
    public IReadOnlyList<string> Candidates { get; init; } = Array.Empty<string>();

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}
#nullable disable
