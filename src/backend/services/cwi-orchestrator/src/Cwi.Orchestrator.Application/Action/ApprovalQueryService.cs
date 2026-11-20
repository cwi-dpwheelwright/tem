#nullable enable
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Action;

public sealed class ApprovalQueryService : DomainServiceBase
{
    private readonly IApprovalTicketRepository _approvalTicketRepository;

    public ApprovalQueryService(IApprovalTicketRepository approvalTicketRepository)
    {
        _approvalTicketRepository = approvalTicketRepository;
    }

    public Task<ServiceResponse<ApprovalTicketStatusDto>> GetStatusAsync(
        string ticketId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleGetAsync(ticketId, tenantId, cancellationToken));
    }

    public Task<ServiceResponse<IReadOnlyList<ApprovalTicketStatusDto>>> ListAsync(
        string tenantId,
        int limit,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleListAsync(tenantId, limit, cancellationToken));
    }

    private async Task<ServiceResponse<ApprovalTicketStatusDto>> HandleGetAsync(
        string ticketId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
        {
            return ServiceResponse<ApprovalTicketStatusDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "ticket_id is required."),
                statusCode: 400,
                message: "ticket_id required");
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<ApprovalTicketStatusDto>.Fail(
                new DomainError(ErrorCodes.Forbidden, "tenant_id is required."),
                statusCode: 403,
                message: "Forbidden");
        }

        var ticket = await _approvalTicketRepository
            .GetByTicketIdAsync(ticketId, cancellationToken)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            return ServiceResponse<ApprovalTicketStatusDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "ticket not found."),
                statusCode: 404,
                message: "ticket not found");
        }

        if (!string.Equals(ticket.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<ApprovalTicketStatusDto>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Access denied."),
                statusCode: 403,
                message: "Forbidden");
        }

        return ServiceResponse<ApprovalTicketStatusDto>.Success(Map(ticket));
    }

    private async Task<ServiceResponse<IReadOnlyList<ApprovalTicketStatusDto>>> HandleListAsync(
        string tenantId,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<IReadOnlyList<ApprovalTicketStatusDto>>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                statusCode: 400,
                message: "tenant_id required");
        }

        var resolvedLimit = Math.Clamp(limit, 1, 200);
        var tickets = await _approvalTicketRepository
            .ListByTenantIdAsync(tenantId, resolvedLimit, cancellationToken)
            .ConfigureAwait(false);

        var results = tickets.Select(Map).ToList();
        return ServiceResponse<IReadOnlyList<ApprovalTicketStatusDto>>.Success(results);
    }

    private static ApprovalTicketStatusDto Map(Cwi.Orchestrator.Domain.Entities.ApprovalTicket ticket)
    {
        return new ApprovalTicketStatusDto
        {
            TicketId = ticket.TicketId,
            Status = ticket.Status,
            Risk = ticket.Risk,
            Reason = ticket.Reason,
            UpdatedUtc = ticket.UpdatedUtc
        };
    }
}
#nullable disable
