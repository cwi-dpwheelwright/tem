using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Ownership;
using Cwi.Orchestrator.Api.BreakGlass;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/ownership")]
public sealed class OwnershipController : ControllerBase
{
    private readonly OwnershipTransferService _service;

    public OwnershipController(OwnershipTransferService service)
    {
        _service = service;
    }

    [HttpPost("transfer")]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<OwnershipTransferResponseDto>> Transfer(
        [FromBody] OwnershipTransferRequestDto request,
        CancellationToken cancellationToken)
    {
        var normalized = new OwnershipTransferRequestDto
        {
            TenantId = request.TenantId,
            FromUserId = request.FromUserId,
            ToUserId = request.ToUserId,
            TransferType = "transfer",
            Reason = request.Reason,
            RequiredApprovals = request.RequiredApprovals,
            ApproverIds = request.ApproverIds
        };

        var result = await _service.CreateAsync(normalized, cancellationToken).ConfigureAwait(false);
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

    [HttpPost("delegate")]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<OwnershipTransferResponseDto>> Delegate(
        [FromBody] OwnershipTransferRequestDto request,
        CancellationToken cancellationToken)
    {
        var normalized = new OwnershipTransferRequestDto
        {
            TenantId = request.TenantId,
            FromUserId = request.FromUserId,
            ToUserId = request.ToUserId,
            TransferType = "delegate",
            Reason = request.Reason,
            RequiredApprovals = request.RequiredApprovals,
            ApproverIds = request.ApproverIds
        };

        var result = await _service.CreateAsync(normalized, cancellationToken).ConfigureAwait(false);
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

    [HttpPost("revoke")]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<OwnershipTransferResponseDto>> Revoke(
        [FromBody] OwnershipTransferRevokeRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RevokeAsync(request, cancellationToken).ConfigureAwait(false);
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
