using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface ICanonicalRecordRepository
{
    Task<CanonicalRecord?> GetAsync(string recordId, CancellationToken cancellationToken);
    Task<CanonicalRecord> UpsertAsync(CanonicalRecord record, CancellationToken cancellationToken);
}
