#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class DocumentVersionDto
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("author")]
    public string Author { get; init; } = string.Empty;

    [JsonPropertyName("checksum")]
    public string Checksum { get; init; } = string.Empty;

    [JsonPropertyName("notes")]
    public string Notes { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

public sealed class DocumentSummaryDto
{
    [JsonPropertyName("doc_id")]
    public string DocId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("versions_count")]
    public int VersionsCount { get; init; }

    [JsonPropertyName("latest_version")]
    public string LatestVersion { get; init; } = string.Empty;
}

public sealed class DocumentDetailDto
{
    [JsonPropertyName("doc_id")]
    public string DocId { get; init; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("owner")]
    public string Owner { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("versions")]
    public IReadOnlyList<DocumentVersionDto> Versions { get; init; } = Array.Empty<DocumentVersionDto>();
}

public sealed class DocumentRollbackRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("actor_id")]
    public string ActorId { get; init; } = string.Empty;

    [JsonPropertyName("target_version")]
    public string TargetVersion { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

public sealed class DocumentRollbackResponseDto
{
    [JsonPropertyName("rollback_id")]
    public string RollbackId { get; init; } = string.Empty;

    [JsonPropertyName("doc_id")]
    public string DocId { get; init; } = string.Empty;

    [JsonPropertyName("target_version")]
    public string TargetVersion { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("requested_by")]
    public string RequestedBy { get; init; } = string.Empty;

    [JsonPropertyName("requested_utc")]
    public DateTimeOffset RequestedUtc { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
#nullable disable
