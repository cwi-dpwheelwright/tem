namespace Cwi.Coordination.DataAccess;

public sealed class SurvivalModeRecord
{
    public Guid Id { get; set; }
    public bool Enabled { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
