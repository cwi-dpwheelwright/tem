namespace Cwi.Coordination.Domain.Entities;

public enum NodeWorkStatus
{
    Queued,
    Leased,
    Acknowledged,
    Completed,
    Failed,
    Denied,
    Expired
}

public sealed class NodeWorkItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string WorkId { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public string? TraceId { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string TaskType { get; set; } = string.Empty;
    public string? PolicyScope { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string? WorkToken { get; set; }
    public string? IdempotencyKey { get; set; }
    public NodeWorkStatus Status { get; set; } = NodeWorkStatus.Queued;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset AvailableUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ExpiresUtc { get; set; }
    public string? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresUtc { get; set; }
    public int LeaseCount { get; set; }
    public string? ResultJson { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
