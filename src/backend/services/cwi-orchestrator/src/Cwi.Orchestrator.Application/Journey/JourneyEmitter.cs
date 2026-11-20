using Cwi.Common.Journey;

namespace Cwi.Orchestrator.Application.Journey;

public sealed class JourneyEmitter
{
    private readonly JourneyRequestContext _context;
    private readonly JourneyEventBusPublisher _publisher;

    public JourneyEmitter(JourneyRequestContext context, JourneyEventBusPublisher publisher)
    {
        _context = context;
        _publisher = publisher;
    }

    public Task EmitAsync(
        string component,
        string phase,
        string direction = "request",
        string? fromComponent = null,
        string? toComponent = null,
        int? statusCode = null,
        long? durationMs = null,
        string? message = null,
        Dictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_context.RequestId))
        {
            return Task.CompletedTask;
        }

        return _publisher.PublishAsync(new JourneyEvent
        {
            RequestId = _context.RequestId!,
            TraceId = _context.TraceId,
            Service = "cwi-orchestrator",
            Component = component,
            Phase = phase,
            Direction = direction,
            FromComponent = fromComponent,
            ToComponent = toComponent,
            TenantId = _context.TenantId,
            UserId = _context.UserId,
            PackName = _context.PackName,
            Method = _context.Method,
            Path = _context.Path,
            StatusCode = statusCode,
            DurationMs = durationMs,
            Message = message,
            Metadata = metadata ?? new Dictionary<string, object?>()
        }, cancellationToken);
    }
}
