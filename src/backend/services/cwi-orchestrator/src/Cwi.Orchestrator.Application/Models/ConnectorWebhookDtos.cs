#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class ConnectorWebhookRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("source_id")]
    public string? SourceId { get; init; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; init; }

    [JsonPropertyName("source_system")]
    public string? SourceSystem { get; init; }

    [JsonPropertyName("source_thread_id")]
    public string? SourceThreadId { get; init; }

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("event_type")]
    public string? EventType { get; init; }

    [JsonPropertyName("occurred_utc")]
    public DateTimeOffset? OccurredUtc { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object?> Metadata { get; init; } = new();

    [JsonPropertyName("items")]
    public JsonElement? Items { get; init; }

    [JsonPropertyName("payload")]
    public JsonElement? Payload { get; init; }

    [JsonPropertyName("normalized_record")]
    public NormalizedConnectorRecordDto? NormalizedRecord { get; init; }

    [JsonPropertyName("normalized_records")]
    public IReadOnlyList<NormalizedConnectorRecordDto>? NormalizedRecords { get; init; }
}

public sealed class ConnectorWebhookResponseDto
{
    [JsonPropertyName("accepted")]
    public bool Accepted { get; init; }

    [JsonPropertyName("connector_type")]
    public string ConnectorType { get; init; } = string.Empty;

    [JsonPropertyName("normalized_items")]
    public int NormalizedItems { get; init; }

    [JsonPropertyName("records_accepted")]
    public int RecordsAccepted { get; init; }

    [JsonPropertyName("records_replayed")]
    public int RecordsReplayed { get; init; }

    [JsonPropertyName("records_rejected")]
    public int RecordsRejected { get; init; }

    [JsonPropertyName("record_ids")]
    public IReadOnlyList<string> RecordIds { get; init; } = Array.Empty<string>();

    [JsonPropertyName("receipts")]
    public IReadOnlyList<ConnectorIngestionReceiptDto> Receipts { get; init; } = Array.Empty<ConnectorIngestionReceiptDto>();

    [JsonPropertyName("doc_id")]
    public string? DocId { get; init; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed record NormalizedConnectorRecordDto
{
    [JsonPropertyName("schema_version")]
    public string SchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("record_id")]
    public string? RecordId { get; init; }

    [JsonPropertyName("tenant_id")]
    public string? TenantId { get; init; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; init; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; init; }

    [JsonPropertyName("connector_type")]
    public string? ConnectorType { get; init; }

    [JsonPropertyName("source_system")]
    public string? SourceSystem { get; init; }

    [JsonPropertyName("source_record_id")]
    public string? SourceRecordId { get; init; }

    [JsonPropertyName("source_thread_id")]
    public string? SourceThreadId { get; init; }

    [JsonPropertyName("source_url")]
    public string? SourceUrl { get; init; }

    [JsonPropertyName("event_time")]
    public DateTimeOffset? EventTime { get; init; }

    [JsonPropertyName("ingested_at")]
    public DateTimeOffset? IngestedAt { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("body")]
    public string? Body { get; init; }

    [JsonPropertyName("raw_excerpt")]
    public string? RawExcerpt { get; init; }

    [JsonPropertyName("participants")]
    public IReadOnlyList<ConnectorParticipantDto> Participants { get; init; } = Array.Empty<ConnectorParticipantDto>();

    [JsonPropertyName("entities")]
    public IReadOnlyList<ConnectorEntityDto> Entities { get; init; } = Array.Empty<ConnectorEntityDto>();

    [JsonPropertyName("candidate_memory_domains")]
    public IReadOnlyList<string> CandidateMemoryDomains { get; init; } = Array.Empty<string>();

    [JsonPropertyName("candidate_memory_types")]
    public IReadOnlyList<string> CandidateMemoryTypes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("life_work_scope")]
    public string? LifeWorkScope { get; init; }

    [JsonPropertyName("sensitivity_label")]
    public string? SensitivityLabel { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    [JsonPropertyName("provenance")]
    public ConnectorProvenanceDto? Provenance { get; init; }

    [JsonPropertyName("sync_state")]
    public ConnectorSyncStateDto? SyncState { get; init; }
}

public sealed class ConnectorParticipantDto
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("role")]
    public string? Role { get; init; }
}

public sealed class ConnectorEntityDto
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }
}

public sealed class ConnectorProvenanceDto
{
    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("provider_record_id")]
    public string? ProviderRecordId { get; init; }

    [JsonPropertyName("provider_account")]
    public string? ProviderAccount { get; init; }

    [JsonPropertyName("source_sha256")]
    public string? SourceSha256 { get; init; }

    [JsonPropertyName("adapter_version")]
    public string? AdapterVersion { get; init; }

    [JsonPropertyName("sync_job_id")]
    public string? SyncJobId { get; init; }

    [JsonPropertyName("retrieved_at")]
    public DateTimeOffset? RetrievedAt { get; init; }
}

public sealed class ConnectorSyncStateDto
{
    [JsonPropertyName("sync_type")]
    public string? SyncType { get; init; }

    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    [JsonPropertyName("is_deleted")]
    public bool IsDeleted { get; init; }

    [JsonPropertyName("is_update")]
    public bool IsUpdate { get; init; }
}

public sealed class ConnectorIngestionReceiptDto
{
    [JsonPropertyName("receipt_id")]
    public string ReceiptId { get; init; } = string.Empty;

    [JsonPropertyName("record_id")]
    public string RecordId { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("connector_type")]
    public string ConnectorType { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("memory_ids_created")]
    public IReadOnlyList<string> MemoryIdsCreated { get; init; } = Array.Empty<string>();

    [JsonPropertyName("memory_ids_updated")]
    public IReadOnlyList<string> MemoryIdsUpdated { get; init; } = Array.Empty<string>();

    [JsonPropertyName("graph_edges_created")]
    public IReadOnlyList<string> GraphEdgesCreated { get; init; } = Array.Empty<string>();

    [JsonPropertyName("error")]
    public string? Error { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; }
}
#nullable disable
