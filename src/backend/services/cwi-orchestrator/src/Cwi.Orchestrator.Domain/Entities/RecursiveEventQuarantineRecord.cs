#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class RecursiveEventQuarantineRecord
{
    public Guid Id { get; set; }
    public string QuarantineId { get; set; } = string.Empty;
    public string? EventId { get; set; }
    public string? TenantId { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "quarantined";
    public string PayloadJson { get; set; } = "null";
    public string? ValidationError { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
#nullable disable
