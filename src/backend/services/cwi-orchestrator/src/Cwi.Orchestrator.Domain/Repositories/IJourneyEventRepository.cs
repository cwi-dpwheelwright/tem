#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IJourneyEventRepository
{
    Task<string?> GetLatestStreamIdAsync(CancellationToken cancellationToken);

    Task<bool> ExistsStreamIdAsync(string streamId, CancellationToken cancellationToken);

    Task AddAsync(JourneyEventRecord record, CancellationToken cancellationToken);

    Task<IReadOnlyList<JourneyEventRecord>> ListByRequestIdAsync(
        string requestId,
        CancellationToken cancellationToken);
}
#nullable disable
