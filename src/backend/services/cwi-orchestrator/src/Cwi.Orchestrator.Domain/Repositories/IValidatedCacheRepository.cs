#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IValidatedCacheRepository
{
    Task<ValidatedCacheRecord?> GetAsync(
        string tenantId,
        string cacheKey,
        CancellationToken cancellationToken);

    Task UpsertAsync(
        ValidatedCacheRecord record,
        CancellationToken cancellationToken);
}
#nullable disable
