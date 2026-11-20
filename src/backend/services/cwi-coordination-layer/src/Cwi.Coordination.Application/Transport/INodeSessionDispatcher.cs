using Cwi.Coordination.Application.Models;

namespace Cwi.Coordination.Application.Transport;

public interface INodeSessionDispatcher
{
    bool HasActiveSession(string nodeId);
    Task<bool> TryDispatchAsync(NodeWorkEnvelopeMessage envelope, CancellationToken cancellationToken);
}

public sealed class NoopNodeSessionDispatcher : INodeSessionDispatcher
{
    public static NoopNodeSessionDispatcher Instance { get; } = new();

    public bool HasActiveSession(string nodeId)
    {
        return false;
    }

    public Task<bool> TryDispatchAsync(NodeWorkEnvelopeMessage envelope, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }
}
