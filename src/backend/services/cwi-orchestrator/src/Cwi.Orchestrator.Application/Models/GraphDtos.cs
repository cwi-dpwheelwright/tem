#nullable enable
using System.Text.Json.Serialization;
using Cwi.Orchestrator.Application.Graph;

namespace Cwi.Orchestrator.Application.Models;

public sealed class GraphQueryRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("question")]
    public string Question { get; init; } = string.Empty;

    [JsonPropertyName("top_k")]
    public int? TopK { get; init; }
}

public sealed class GraphQueryResponseDto
{
    [JsonPropertyName("template")]
    public string? Template { get; init; }

    [JsonPropertyName("terms")]
    public IReadOnlyList<string> Terms { get; init; } = Array.Empty<string>();

    [JsonPropertyName("evidence")]
    public IReadOnlyList<GraphEvidenceDto> Evidence { get; init; } = Array.Empty<GraphEvidenceDto>();
}

public sealed class GraphEvidenceDto
{
    [JsonPropertyName("subject")]
    public string Subject { get; init; } = string.Empty;

    [JsonPropertyName("relation")]
    public string Relation { get; init; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; init; } = string.Empty;

    [JsonPropertyName("template")]
    public string Template { get; init; } = string.Empty;

    [JsonPropertyName("source_document")]
    public string? SourceDocument { get; init; }

    [JsonPropertyName("source_pack_target")]
    public string? SourcePackTarget { get; init; }

    [JsonPropertyName("target_pack_target")]
    public string? TargetPackTarget { get; init; }

    [JsonPropertyName("inferred")]
    public bool Inferred { get; init; }
}

public sealed class UserGraphProfileResponseDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("sections")]
    public IReadOnlyList<UserGraphProfileSectionDto> Sections { get; init; } = Array.Empty<UserGraphProfileSectionDto>();
}

public sealed class UserGraphProfileSectionDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("evidence")]
    public IReadOnlyList<GraphEvidenceDto> Evidence { get; init; } = Array.Empty<GraphEvidenceDto>();
}
#nullable disable
