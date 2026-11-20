using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface ILedgerRepository
{
    Task<string?> GetLatestHashAsync(string traceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LedgerEvent>> GetByTraceIdAsync(string traceId, int skip, int take, CancellationToken cancellationToken);
    Task<int> GetCountAsync(string traceId, CancellationToken cancellationToken);
    Task<IReadOnlyList<LedgerEvent>> SearchAsync(
        DateTimeOffset? startUtc,
        DateTimeOffset? endUtc,
        string? eventType,
        CancellationToken cancellationToken);
    Task AppendAsync(LedgerEvent ledgerEvent, CancellationToken cancellationToken);
}
