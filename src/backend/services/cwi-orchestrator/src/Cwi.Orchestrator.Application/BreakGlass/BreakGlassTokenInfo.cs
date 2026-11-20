#nullable enable
namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassTokenInfo
{
    public string TokenId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public DateTimeOffset ExpiresUtc { get; init; }
}
#nullable disable
