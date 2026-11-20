#nullable enable
namespace Cwi.Orchestrator.Application.Packs.Lifecycle;

public sealed class PackLifecycleAuditPayload
{
    public string? TenantId { get; init; }
    public string PackId { get; init; } = string.Empty;
    public string? PackVersion { get; init; }
    public IReadOnlyList<string>? DeletedVersions { get; init; }
    public string? Latest { get; init; }
    public string ActorUserId { get; init; } = string.Empty;
    public string? Reason { get; init; }
    public string Action { get; init; } = string.Empty;
    public DateTimeOffset TimestampUtc { get; init; }
}
#nullable disable
