namespace Cwi.Orchestrator.Domain.Entities;

public sealed class ConnectorRun
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string ConnectorKind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Cursor { get; set; } = string.Empty;
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset? CompletedUtc { get; set; }
}
