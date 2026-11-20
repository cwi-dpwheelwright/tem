#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class ExternalOpinionRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("trace_id")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("policy_status")]
    public string PolicyStatus { get; init; } = "observe";

    [JsonPropertyName("prompt_summary")]
    public string PromptSummary { get; init; } = string.Empty;

    [JsonPropertyName("context_summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ContextSummary { get; init; }

    [JsonPropertyName("governed_answer_hash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GovernedAnswerHash { get; init; }

    [JsonPropertyName("evidence_hashes")]
    public IReadOnlyList<string> EvidenceHashes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("purpose")]
    public string Purpose { get; init; } = "response_quality_review";
}

public sealed class ExternalOpinionFindingDto
{
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "info";

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed class ExternalOpinionResponseDto
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("advisory_only")]
    public bool AdvisoryOnly { get; init; } = true;

    [JsonPropertyName("applied_to_answer")]
    public bool AppliedToAnswer { get; init; }

    [JsonPropertyName("external_submission_status")]
    public string ExternalSubmissionStatus { get; init; } = "not_sent";

    [JsonPropertyName("external_status_code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExternalStatusCode { get; init; }

    [JsonPropertyName("findings")]
    public IReadOnlyList<ExternalOpinionFindingDto> Findings { get; init; } = Array.Empty<ExternalOpinionFindingDto>();

    [JsonPropertyName("reasons")]
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    [JsonPropertyName("trace_id")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("recorded_utc")]
    public DateTimeOffset RecordedUtc { get; init; }
}
#nullable disable
