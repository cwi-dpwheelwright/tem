#nullable enable
namespace Cwi.Orchestrator.Application.Packs.Activation;

public sealed class PackActivationRequestDto
{
    public string TenantId { get; init; } = string.Empty;
    public string PackName { get; init; } = string.Empty;
    public string PackHash { get; init; } = string.Empty;
    public string ActorUserId { get; init; } = string.Empty;
}

public sealed class PackActivationResponseDto
{
    public string TenantId { get; init; } = string.Empty;
    public string PackName { get; init; } = string.Empty;
    public string PackHash { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset ActivatedUtc { get; init; }
    public DateTimeOffset? RolledBackUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}

public sealed class PackRollbackRequestDto
{
    public string TenantId { get; init; } = string.Empty;
    public string PackName { get; init; } = string.Empty;
    public string ActorUserId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class PackRollbackResponseDto
{
    public string TenantId { get; init; } = string.Empty;
    public string PackName { get; init; } = string.Empty;
    public string PackHash { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset? RolledBackUtc { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}
#nullable disable
