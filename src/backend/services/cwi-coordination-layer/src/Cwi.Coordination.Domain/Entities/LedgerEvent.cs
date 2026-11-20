namespace Cwi.Coordination.Domain.Entities;

public sealed class LedgerEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string EventType { get; init; } = string.Empty;
    public string PayloadJson { get; init; } = string.Empty;
    public string PrevHash { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
