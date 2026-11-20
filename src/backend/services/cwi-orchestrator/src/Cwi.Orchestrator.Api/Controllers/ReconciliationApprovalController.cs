#nullable enable
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Reconciliation;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/reconciliation/records/{recordId}/approval/complete")]
public sealed class ReconciliationApprovalController : ControllerBase
{
    private readonly ReconciliationApprovalResolutionService _resolutionService;

    public ReconciliationApprovalController(ReconciliationApprovalResolutionService resolutionService)
    {
        _resolutionService = resolutionService;
    }

    [HttpPost]
    public async Task<ActionResult<ReconciliationApprovalCompleteResponseDto>> Complete(
        [FromRoute] string recordId,
        [FromBody] ApprovalCompleteRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _resolutionService
            .CompleteAsync(recordId, request, cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
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
#nullable disable
