#nullable enable
namespace Cwi.Orchestrator.Application.Packs.Lifecycle;

public sealed class PackRetireRequestDto
{
    public string PackId { get; init; } = string.Empty;
    public string ActorUserId { get; init; } = string.Empty;
    public string? TenantId { get; init; }
    public string? Reason { get; init; }
}

public sealed class PackRetireResponseDto
{
    public string PackId { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool Retired { get; init; }
    public DateTimeOffset RetiredUtc { get; init; }
}

public sealed class PackDeleteRequestDto
{
    public string PackId { get; init; } = string.Empty;
    public string? PackVersion { get; init; }
    public string ActorUserId { get; init; } = string.Empty;
    public string? TenantId { get; init; }
    public string? Reason { get; init; }
}

public sealed class PackDeleteResponseDto
{
    public string PackId { get; init; } = string.Empty;
    public string? PackVersion { get; init; }
    public string Status { get; init; } = string.Empty;
    public IReadOnlyList<string> DeletedVersions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RemainingVersions { get; init; } = Array.Empty<string>();
    public string? Latest { get; init; }
    public DateTimeOffset DeletedUtc { get; init; }
}
#nullable disable
