#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public class ChatImportSummaryDto
{
    [JsonPropertyName("import_id")]
    public string ImportId { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("source_type")]
    public string SourceType { get; init; } = string.Empty;

    [JsonPropertyName("storage_location")]
    public string StorageLocation { get; init; } = string.Empty;

    [JsonPropertyName("provenance_id")]
    public string? ProvenanceId { get; init; }

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class ChatImportDetailDto : ChatImportSummaryDto
{
    [JsonPropertyName("chunks")]
    public IReadOnlyList<ChatImportChunkDto> Chunks { get; init; } = Array.Empty<ChatImportChunkDto>();
}

public sealed class ChatImportChunkDto
{
    [JsonPropertyName("chunk_id")]
    public string ChunkId { get; init; } = string.Empty;

    [JsonPropertyName("chunk_index")]
    public int ChunkIndex { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("content_sha256")]
    public string ContentSha256 { get; init; } = string.Empty;

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }
}
#nullable disable
