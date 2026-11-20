#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class SourceMemoryRecord
{
    public Guid Id { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? ConversationId { get; set; }
    public string? NodeId { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourceType { get; set; } = "uploaded_source";
    public string StorageLocation { get; set; } = "orchestrator_document_store";
    public string ExtractionMethod { get; set; } = "server_side_text_ingest";
    public bool LocalOnly { get; set; }
    public bool SyncAllowed { get; set; } = true;
    public string ContentSha256 { get; set; } = string.Empty;
    public string? ProvenanceId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
#nullable disable
