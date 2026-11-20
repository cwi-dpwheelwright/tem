namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassNotification
{
    public string EventType { get; init; } = string.Empty;
    public string TokenId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public string Scope { get; init; } = string.Empty;
    public DateTimeOffset OccurredUtc { get; init; }
}
