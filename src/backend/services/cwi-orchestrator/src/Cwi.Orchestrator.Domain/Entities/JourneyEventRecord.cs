#nullable enable
namespace Cwi.Orchestrator.Domain.Entities;

public sealed class JourneyEventRecord
{
    public long Id { get; set; }
    public string EventId { get; set; } = string.Empty;
    public string StreamId { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public string? TraceId { get; set; }
    public string Service { get; set; } = "unknown";
    public string Component { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string Direction { get; set; } = "request";
    public string? FromComponent { get; set; }
    public string? ToComponent { get; set; }
    public string? TenantId { get; set; }
    public string? UserId { get; set; }
    public string? PackName { get; set; }
    public string? Method { get; set; }
    public string? Path { get; set; }
    public int? StatusCode { get; set; }
    public long? DurationMs { get; set; }
    public string? Message { get; set; }
    public string? MetadataJson { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
}
#nullable disable
