#nullable enable
namespace Cwi.Orchestrator.Application.Packs.Lifecycle;

public sealed class PackAuditQuery
{
    public DateTimeOffset? StartUtc { get; init; }
    public DateTimeOffset? EndUtc { get; init; }
    public string? TenantId { get; init; }
    public string? PackId { get; init; }
    public string? ActorUserId { get; init; }
    public string? EventType { get; init; }
    public int Limit { get; init; } = 100;
}

public sealed class PackAuditEventDto
{
    public string TraceId { get; init; } = string.Empty;
    public string EventType { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; }
}

public sealed class PackAuditResponseDto
{
    public int Total { get; init; }
    public int Limit { get; init; }
    public IReadOnlyList<PackAuditEventDto> Items { get; init; } = Array.Empty<PackAuditEventDto>();
}
#nullable disable
