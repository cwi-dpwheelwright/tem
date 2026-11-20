#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class ConnectorIngestionReceipt
{
    public Guid Id { get; set; }
    public string ReceiptId { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ConnectorType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string MemoryIdsCreatedJson { get; set; } = "[]";
    public string MemoryIdsUpdatedJson { get; set; } = "[]";
    public string GraphEdgesCreatedJson { get; set; } = "[]";
    public string? Error { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
#nullable disable
