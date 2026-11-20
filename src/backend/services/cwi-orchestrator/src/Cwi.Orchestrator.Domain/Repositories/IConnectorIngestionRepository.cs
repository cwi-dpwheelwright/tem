#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IConnectorIngestionRepository
{
    Task<NormalizedConnectorRecordEntity?> GetRecordAsync(
        string tenantId,
        string recordId,
        string userId,
        CancellationToken cancellationToken);

    Task UpsertRecordAsync(
        NormalizedConnectorRecordEntity record,
        CancellationToken cancellationToken);

    Task<MemoryFactIndexRecord?> GetMemoryFactAsync(
        string tenantId,
        string factKey,
        string userId,
        CancellationToken cancellationToken);

    Task UpsertMemoryFactAsync(
        MemoryFactIndexRecord fact,
        CancellationToken cancellationToken);

    Task AddReceiptAsync(
        ConnectorIngestionReceipt receipt,
        CancellationToken cancellationToken);
}
#nullable disable
