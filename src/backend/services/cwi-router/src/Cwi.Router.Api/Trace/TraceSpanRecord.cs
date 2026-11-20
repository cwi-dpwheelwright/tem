namespace Cwi.Router.Api.Trace;

public sealed class TraceSpanRecord
{
    public string TraceId { get; init; } = string.Empty;
    public string SpanId { get; init; } = string.Empty;
    public string? ParentSpanId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public DateTimeOffset StartUtc { get; init; }
    public double DurationMs { get; init; }
    public string? Status { get; init; }
}
