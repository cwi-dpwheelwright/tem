#nullable enable
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IOwnershipTransferRepository
{
    Task<OwnershipTransferRequest?> GetByRequestIdAsync(string requestId, CancellationToken cancellationToken);

    Task<OwnershipTransferRequest?> GetByApprovalTicketIdAsync(string approvalTicketId, CancellationToken cancellationToken);

    Task<OwnershipTransferRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<OwnershipTransferRequest>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken);

    Task AddAsync(OwnershipTransferRequest request, CancellationToken cancellationToken);

    Task UpdateAsync(OwnershipTransferRequest request, CancellationToken cancellationToken);
}
#nullable disable
