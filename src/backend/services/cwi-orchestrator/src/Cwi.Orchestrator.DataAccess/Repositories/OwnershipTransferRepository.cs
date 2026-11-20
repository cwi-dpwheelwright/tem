using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class OwnershipTransferRepository : IOwnershipTransferRepository
{
    private readonly OrchestratorDbContext _db;

    public OwnershipTransferRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<OwnershipTransferRequest?> GetByRequestIdAsync(string requestId, CancellationToken cancellationToken)
    {
        return _db.OwnershipTransferRequests
            .FirstOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
    }

    public Task<OwnershipTransferRequest?> GetByApprovalTicketIdAsync(string approvalTicketId, CancellationToken cancellationToken)
    {
        return _db.OwnershipTransferRequests
            .FirstOrDefaultAsync(x => x.ApprovalTicketId == approvalTicketId, cancellationToken);
    }

    public Task<OwnershipTransferRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        return _db.OwnershipTransferRequests
            .FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task<IReadOnlyList<OwnershipTransferRequest>> ListByTenantAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Array.Empty<OwnershipTransferRequest>();
        }

        return await _db.OwnershipTransferRequests
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.UpdatedUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(OwnershipTransferRequest request, CancellationToken cancellationToken)
    {
        _db.OwnershipTransferRequests.Add(request);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(OwnershipTransferRequest request, CancellationToken cancellationToken)
    {
        _db.OwnershipTransferRequests.Update(request);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
