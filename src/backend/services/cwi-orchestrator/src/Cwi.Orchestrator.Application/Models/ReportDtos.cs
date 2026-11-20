#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class ReportEvidenceDto
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;
}

public class ReportSummaryDto
{
    [JsonPropertyName("report_id")]
    public string ReportId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("coherence_score")]
    public double CoherenceScore { get; init; }

    [JsonPropertyName("conflict_count")]
    public int ConflictCount { get; init; }

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("escalation_ticket")]
    public string? EscalationTicket { get; init; }
}

public sealed class ReportDetailDto : ReportSummaryDto
{
    [JsonPropertyName("narrative")]
    public string Narrative { get; init; } = string.Empty;

    [JsonPropertyName("structured")]
    public IReadOnlyDictionary<string, string> Structured { get; init; } =
        new Dictionary<string, string>();

    [JsonPropertyName("evidence")]
    public IReadOnlyList<ReportEvidenceDto> Evidence { get; init; } =
        Array.Empty<ReportEvidenceDto>();
}
#nullable disable
