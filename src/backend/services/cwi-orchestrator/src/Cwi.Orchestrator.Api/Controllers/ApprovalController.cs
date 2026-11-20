#nullable enable
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Action;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("mcp/approval/complete")]
[Route("mcp/action/approval/complete")]
[Route("api/approval/complete")]
public sealed class ApprovalController : ControllerBase
{
    private readonly ApprovalService _approvalService;

    public ApprovalController(ApprovalService approvalService)
    {
        _approvalService = approvalService;
    }

    [HttpPost]
    public async Task<ActionResult<ApprovalCompleteResponseDto>> Complete(
        [FromBody] ApprovalCompleteRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _approvalService.CompleteAsync(request, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }
}
#nullable disable
