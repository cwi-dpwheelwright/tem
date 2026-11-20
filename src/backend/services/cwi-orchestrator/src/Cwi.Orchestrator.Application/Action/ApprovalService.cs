#nullable enable
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Action;

public sealed class ApprovalService : DomainServiceBase
{
    private readonly IApprovalTicketRepository _approvalTicketRepository;
    private readonly Ownership.OwnershipTransferService _ownershipTransferService;

    public ApprovalService(
        IApprovalTicketRepository approvalTicketRepository,
        Ownership.OwnershipTransferService ownershipTransferService)
    {
        _approvalTicketRepository = approvalTicketRepository;
        _ownershipTransferService = ownershipTransferService;
    }

    public Task<ServiceResponse<ApprovalCompleteResponseDto>> CompleteAsync(
        ApprovalCompleteRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleAsync(request, cancellationToken));
    }

    private async Task<ServiceResponse<ApprovalCompleteResponseDto>> HandleAsync(
        ApprovalCompleteRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TicketId))
        {
            return ServiceResponse<ApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "ticket_id is required."),
                statusCode: 400,
                message: "ticket_id required");
        }

        if (string.IsNullOrWhiteSpace(request.Decision))
        {
            return ServiceResponse<ApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "decision is required."),
                statusCode: 400,
                message: "decision required");
        }

        var decision = request.Decision.Trim().ToLowerInvariant();
        if (decision is not ("approve" or "deny"))
        {
            return ServiceResponse<ApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "decision must be approve or deny."),
                statusCode: 400,
                message: "invalid decision");
        }

        var ticket = await _approvalTicketRepository
            .GetByTicketIdAsync(request.TicketId, cancellationToken)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            return ServiceResponse<ApprovalCompleteResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "ticket not found."),
                statusCode: 404,
                message: "ticket not found");
        }

        ticket.Status = decision == "approve" ? "approved" : "denied";
        ticket.UpdatedUtc = DateTimeOffset.UtcNow;
        await _approvalTicketRepository.UpdateAsync(ticket, cancellationToken).ConfigureAwait(false);
        await _ownershipTransferService
            .ApplyApprovalDecisionAsync(ticket.TicketId, decision, request.ApproverId, request.Reason, cancellationToken)
            .ConfigureAwait(false);

        var response = new ApprovalCompleteResponseDto
        {
            TicketId = ticket.TicketId,
            Status = ticket.Status,
            Executed = false,
            Trace = new TracePayload
            {
                TraceId = Guid.NewGuid().ToString("N"),
                Sources = new List<string> { $"approval_ticket:{ticket.TicketId}" },
                Confidence = 0.0f,
                PolicyMode = string.Empty,
                Model = string.Empty,
                PackHash = string.Empty
            }
        };

        return ServiceResponse<ApprovalCompleteResponseDto>.Success(response);
    }
}
#nullable disable
