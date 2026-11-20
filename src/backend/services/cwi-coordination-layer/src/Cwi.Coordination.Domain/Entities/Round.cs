namespace Cwi.Coordination.Domain.Entities;

public enum RoundStatus
{
    Pending,
    Active,
    Closed
}

public sealed class Round
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public RoundStatus Status { get; set; } = RoundStatus.Pending;
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ClosedUtc { get; set; }
}
