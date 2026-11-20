using Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Domain.Repositories;

public interface IMeshTopologyRepository
{
    Task UpsertAsync(LinkState state, CancellationToken cancellationToken);
    Task<LinkState?> GetAsync(string nodeId, InterfaceKind interfaceKind, CancellationToken cancellationToken);
    Task<IReadOnlyList<LinkState>> ListAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<LinkState>> ListByNodeAsync(string nodeId, CancellationToken cancellationToken);
}
