namespace Cwi.Coordination.Domain.Entities;

public sealed class DegradationEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventId { get; set; } = string.Empty;
    public string NodeId { get; set; } = string.Empty;
    public DateTimeOffset ObservedUtc { get; set; }
    public double Severity { get; set; }
    public Dictionary<string, object?> Details { get; set; } = new();
    public Dictionary<string, object?>? Provenance { get; set; }
    public DateTimeOffset ReceivedUtc { get; set; } = DateTimeOffset.UtcNow;
}
