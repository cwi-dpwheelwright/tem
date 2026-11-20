namespace Cwi.Orchestrator.Domain.Entities;

public sealed class TelemetryWindow
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public DateTimeOffset WindowStartUtc { get; set; }
    public DateTimeOffset WindowEndUtc { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
}
