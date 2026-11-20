#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class SourceChunkRecord
{
    public Guid Id { get; set; }
    public string ChunkId { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? ConversationId { get; set; }
    public string? NodeId { get; set; }
    public string ChunkText { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string? HeadingContext { get; set; }
    public bool LocalOnly { get; set; }
    public string ContentSha256 { get; set; } = string.Empty;
    public string? EmbeddingId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
#nullable disable
