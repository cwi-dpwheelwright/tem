using System.Collections.Concurrent;
using System.Threading.Channels;
using Cwi.Common.Journey;

namespace Cwi.Orchestrator.Api.Journey;

public sealed class JourneyBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();

    public JourneySubscription Subscribe(string requestId)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<JourneyEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _subscriptions[id] = new Subscription(requestId, channel);
        return new JourneySubscription(channel.Reader, () => Unsubscribe(id));
    }

    public void Publish(JourneyEvent journeyEvent)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            if (!string.Equals(subscription.RequestId, journeyEvent.RequestId, StringComparison.Ordinal))
            {
                continue;
            }

            subscription.Channel.Writer.TryWrite(journeyEvent);
        }
    }

    private void Unsubscribe(Guid id)
    {
        if (_subscriptions.TryRemove(id, out var subscription))
        {
            subscription.Channel.Writer.TryComplete();
        }
    }

    private sealed record Subscription(string RequestId, Channel<JourneyEvent> Channel);
}

public sealed class JourneySubscription : IAsyncDisposable
{
    private readonly Action _dispose;

    public JourneySubscription(ChannelReader<JourneyEvent> reader, Action dispose)
    {
        Reader = reader;
        _dispose = dispose;
    }

    public ChannelReader<JourneyEvent> Reader { get; }

    public ValueTask DisposeAsync()
    {
        _dispose();
        return ValueTask.CompletedTask;
    }
}
