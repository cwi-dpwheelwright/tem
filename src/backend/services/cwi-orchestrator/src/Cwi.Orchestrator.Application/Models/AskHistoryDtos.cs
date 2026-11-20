using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class AskHistoryRecordRequestDto
{
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    [JsonPropertyName("conversation_id")]
    public string? ConversationId { get; init; }

    [JsonPropertyName("question")]
    public string Question { get; init; } = string.Empty;

    [JsonPropertyName("answer")]
    public string Answer { get; init; } = string.Empty;

    [JsonPropertyName("response_status")]
    public string? ResponseStatus { get; init; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }

    [JsonPropertyName("pack_name")]
    public string? PackName { get; init; }

    [JsonPropertyName("pack_hash")]
    public string? PackHash { get; init; }

    [JsonPropertyName("trace")]
    public JsonElement? Trace { get; init; }

    [JsonPropertyName("attachments")]
    public IReadOnlyList<AskHistoryAttachmentDto> Attachments { get; init; } = Array.Empty<AskHistoryAttachmentDto>();

    [JsonPropertyName("max_entries")]
    public int? MaxEntries { get; init; }

    [JsonPropertyName("retention_days")]
    public int? RetentionDays { get; init; }
}

public sealed class AskHistoryRecordDto
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    [JsonPropertyName("conversation_id")]
    public string ConversationId { get; init; } = "default";

    [JsonPropertyName("turn_id")]
    public string TurnId { get; init; } = string.Empty;

    [JsonPropertyName("question")]
    public string Question { get; init; } = string.Empty;

    [JsonPropertyName("answer")]
    public string Answer { get; init; } = string.Empty;

    [JsonPropertyName("response_status")]
    public string ResponseStatus { get; init; } = "completed";

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }

    [JsonPropertyName("provenance_id")]
    public string? ProvenanceId { get; init; }

    [JsonPropertyName("pack_name")]
    public string? PackName { get; init; }

    [JsonPropertyName("pack_hash")]
    public string? PackHash { get; init; }

    [JsonPropertyName("content_sha256")]
    public string ContentSha256 { get; init; } = string.Empty;

    [JsonPropertyName("summary_status")]
    public string SummaryStatus { get; init; } = "pending";

    [JsonPropertyName("trace")]
    public JsonElement? Trace { get; init; }

    [JsonPropertyName("attachments")]
    public IReadOnlyList<AskHistoryAttachmentDto> Attachments { get; init; } = Array.Empty<AskHistoryAttachmentDto>();
}

public sealed class AskHistoryAttachmentDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("mime_type")]
    public string MimeType { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("excerpt")]
    public string? Excerpt { get; init; }

    [JsonPropertyName("truncated")]
    public bool? Truncated { get; init; }

    [JsonPropertyName("preview_data_url")]
    public string? PreviewDataUrl { get; init; }
}

public sealed class AskHistoryListResponseDto
{
    [JsonPropertyName("items")]
    public IReadOnlyList<AskHistoryRecordDto> Items { get; init; } = Array.Empty<AskHistoryRecordDto>();
}

public sealed class AskHistoryClearResponseDto
{
    [JsonPropertyName("deleted")]
    public int Deleted { get; init; }
}
