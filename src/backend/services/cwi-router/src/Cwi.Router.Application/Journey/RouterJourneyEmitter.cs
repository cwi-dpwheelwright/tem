using Cwi.Common.Journey;
using Cwi.Router.Application.EventBus;

namespace Cwi.Router.Application.Journey;

public sealed class RouterJourneyEmitter
{
    private readonly JourneyRequestContext _context;
    private readonly EventBusPublisher _publisher;

    public RouterJourneyEmitter(JourneyRequestContext context, EventBusPublisher publisher)
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

        var journeyEvent = new JourneyEvent
        {
            RequestId = _context.RequestId!,
            TraceId = _context.TraceId,
            Service = "cwi-router",
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
        };

        return _publisher.PublishAsync(journeyEvent.ToEnvelope(), cancellationToken);
    }
}
