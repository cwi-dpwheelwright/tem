using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class ApprovalTicketRepository : IApprovalTicketRepository
{
    private readonly OrchestratorDbContext _db;

    public ApprovalTicketRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public Task<ApprovalTicket?> GetByTicketIdAsync(string ticketId, CancellationToken cancellationToken)
    {
        return _db.ApprovalTickets.FirstOrDefaultAsync(x => x.TicketId == ticketId, cancellationToken);
    }

    public Task<ApprovalTicket?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        return _db.ApprovalTickets.FirstOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
    }

    public async Task<IReadOnlyList<ApprovalTicket>> ListByTenantIdAsync(string tenantId, int limit, CancellationToken cancellationToken)
    {
        return await _db.ApprovalTickets
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(ApprovalTicket ticket, CancellationToken cancellationToken)
    {
        _db.ApprovalTickets.Add(ticket);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ApprovalTicket ticket, CancellationToken cancellationToken)
    {
        _db.ApprovalTickets.Update(ticket);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
