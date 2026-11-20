#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class OutcomeFeedbackRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string TraceId { get; set; } = string.Empty;
    public string? TicketId { get; set; }
    public string? ActionId { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string FeedbackType { get; set; } = string.Empty;
    public bool ConsentGranted { get; set; }
    public string? Notes { get; set; }
    public double? ConfidenceBefore { get; set; }
    public double? ConfidenceAfter { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
#nullable disable
