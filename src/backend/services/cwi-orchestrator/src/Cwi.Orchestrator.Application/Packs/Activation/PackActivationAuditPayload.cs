#nullable enable
namespace Cwi.Orchestrator.Application.Packs.Activation;

public sealed class PackActivationAuditPayload
{
    public string TenantId { get; init; } = string.Empty;
    public string PackName { get; init; } = string.Empty;
    public string PackHash { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string ActivatedBy { get; init; } = string.Empty;
    public string RolledBackBy { get; init; } = string.Empty;
    public string RollbackReason { get; init; } = string.Empty;
    public DateTimeOffset ActivatedUtc { get; init; }
    public DateTimeOffset? RolledBackUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}
#nullable disable
