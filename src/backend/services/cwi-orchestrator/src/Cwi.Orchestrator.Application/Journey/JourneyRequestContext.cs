namespace Cwi.Orchestrator.Application.Journey;

public sealed class JourneyRequestContext
{
    public string? RequestId { get; set; }
    public string? TraceId { get; set; }
    public string? TenantId { get; set; }
    public string? UserId { get; set; }
    public string? PackName { get; set; }
    public string? Method { get; set; }
    public string? Path { get; set; }
}
