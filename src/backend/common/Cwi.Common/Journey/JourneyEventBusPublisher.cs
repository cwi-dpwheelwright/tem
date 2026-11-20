using Cwi.Common.Events;

namespace Cwi.Common.Journey;

public sealed class JourneyEventBusPublisher
{
    private readonly EventBusClient _eventBusClient;
    private readonly EventBusClientOptions _options;

    public JourneyEventBusPublisher(EventBusClient eventBusClient, EventBusClientOptions options)
    {
        _eventBusClient = eventBusClient;
        _options = options;
    }

    public Task PublishAsync(JourneyEvent journeyEvent, CancellationToken cancellationToken = default)
    {
        var service = string.IsNullOrWhiteSpace(journeyEvent.Service)
            ? _options.ServiceName
            : journeyEvent.Service;

        var envelope = (journeyEvent with { Service = service }).ToEnvelope(_options.Environment);
        return _eventBusClient.PublishAsync(envelope, cancellationToken);
    }
}
