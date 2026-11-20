using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IApprovalTicketRepository
{
    Task<ApprovalTicket?> GetByTicketIdAsync(string ticketId, CancellationToken cancellationToken);
    Task<ApprovalTicket?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApprovalTicket>> ListByTenantIdAsync(string tenantId, int limit, CancellationToken cancellationToken);
    Task AddAsync(ApprovalTicket ticket, CancellationToken cancellationToken);
    Task UpdateAsync(ApprovalTicket ticket, CancellationToken cancellationToken);
}
