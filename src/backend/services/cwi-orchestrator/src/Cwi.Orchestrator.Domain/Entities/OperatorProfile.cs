namespace Cwi.Orchestrator.Domain.Entities;

public sealed class OperatorProfile
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string OperatorId { get; set; } = string.Empty;
    public int SignalsTotal { get; set; }
    public int CorrectionsTotal { get; set; }
    public int DelaysTotal { get; set; }
    public int ConfirmationsTotal { get; set; }
    public double LatencySum { get; set; }
    public int LatencyCount { get; set; }
    public double CorrelationScore { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; }
}
