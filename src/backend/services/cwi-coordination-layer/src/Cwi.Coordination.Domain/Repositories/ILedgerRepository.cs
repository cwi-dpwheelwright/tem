using Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Domain.Repositories;

public interface ILedgerRepository
{
    Task<LedgerEvent> AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<LedgerEvent>> ListAsync(int limit, CancellationToken cancellationToken);
}
