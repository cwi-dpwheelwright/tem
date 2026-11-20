using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Ownership;
using Cwi.Orchestrator.Application.Action;
using Cwi.Orchestrator.Api.BreakGlass;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/ownership/transfers")]
public sealed class OwnershipTransferController : ControllerBase
{
    private readonly OwnershipTransferService _service;
    private readonly ApprovalService _approvalService;

    public OwnershipTransferController(OwnershipTransferService service, ApprovalService approvalService)
    {
        _service = service;
        _approvalService = approvalService;
    }

    [HttpPost]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<OwnershipTransferResponseDto>> Create(
        [FromBody] OwnershipTransferRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "invalid_request",
                message = result.Error?.Message ?? result.Message
            });
        }

        return Ok(result.Data);
    }

    [HttpGet("{requestId}")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<OwnershipTransferResponseDto>> Get(
        [FromRoute] string requestId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(requestId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "invalid_request",
                message = result.Error?.Message ?? result.Message
            });
        }

        return Ok(result.Data);
    }

    [HttpPost("{requestId}/decision")]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<ApprovalCompleteResponseDto>> Decide(
        [FromRoute] string requestId,
        [FromBody] OwnershipTransferDecisionRequestDto decision,
        CancellationToken cancellationToken)
    {
        var approvalRequest = new ApprovalCompleteRequestDto
        {
            TicketId = requestId,
            Decision = decision.Decision,
            ApproverId = decision.ApproverId,
            Reason = decision.Reason
        };
        var result = await _approvalService.CompleteAsync(approvalRequest, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "invalid_request",
                message = result.Error?.Message ?? result.Message
            });
        }

        return Ok(result.Data);
    }
}
