using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.DataAccess;

public sealed class InMemoryMeshTopologyRepository : IMeshTopologyRepository
{
    private readonly Dictionary<string, LinkState> _states = new(StringComparer.OrdinalIgnoreCase);

    public Task UpsertAsync(LinkState state, CancellationToken cancellationToken)
    {
        var key = BuildKey(state.NodeId, state.Interface);
        _states[key] = state;
        return Task.CompletedTask;
    }

    public Task<LinkState?> GetAsync(string nodeId, InterfaceKind interfaceKind, CancellationToken cancellationToken)
    {
        _states.TryGetValue(BuildKey(nodeId, interfaceKind), out var state);
        return Task.FromResult(state);
    }

    public Task<IReadOnlyList<LinkState>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult((IReadOnlyList<LinkState>)_states.Values.ToList());
    }

    public Task<IReadOnlyList<LinkState>> ListByNodeAsync(string nodeId, CancellationToken cancellationToken)
    {
        var result = _states.Values
            .Where(state => string.Equals(state.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult((IReadOnlyList<LinkState>)result);
    }

    private static string BuildKey(string nodeId, InterfaceKind interfaceKind)
        => $"{nodeId}:{interfaceKind}";
}
