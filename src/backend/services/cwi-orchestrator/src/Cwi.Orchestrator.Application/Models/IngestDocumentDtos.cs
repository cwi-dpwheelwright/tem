#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class IngestDocumentRequestDto
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "v1";

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; set; }

    [JsonPropertyName("idempotency_key")]
    public string? IdempotencyKey { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("document")]
    public IngestDocumentPayloadDto Document { get; set; } = new();
}

public sealed class IngestDocumentPayloadDto
{
    [JsonPropertyName("doc_id")]
    public string DocId { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("mime_type")]
    public string MimeType { get; set; } = "text/plain";

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("content_encoding")]
    public string ContentEncoding { get; set; } = "plain";

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("created_utc")]
    public DateTimeOffset? CreatedUtc { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?> Metadata { get; set; } = new();
}

public sealed class IngestDocumentResponseDto
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; set; }

    [JsonPropertyName("ingest_id")]
    public string IngestId { get; set; } = string.Empty;

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; set; }

    [JsonPropertyName("doc_id")]
    public string? DocId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
#nullable disable
