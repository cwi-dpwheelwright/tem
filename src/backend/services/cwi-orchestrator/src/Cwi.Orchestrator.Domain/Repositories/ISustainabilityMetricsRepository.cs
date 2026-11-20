#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface ISustainabilityMetricsRepository
{
    Task<SustainabilityRequestRecord?> GetByRequestIdAsync(
        string requestId,
        CancellationToken cancellationToken);

    Task AddAsync(
        SustainabilityRequestRecord record,
        CancellationToken cancellationToken);

    Task UpsertAsync(
        SustainabilityRequestRecord record,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<SustainabilityRequestRecord>> QueryAsync(
        string? tenantId,
        string? userIdHash,
        string? nodeId,
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtc,
        int skip,
        int take,
        CancellationToken cancellationToken);
}
#nullable disable
