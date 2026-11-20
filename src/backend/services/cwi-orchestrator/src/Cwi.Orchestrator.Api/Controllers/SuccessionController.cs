#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Ownership;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/succession")]
public sealed class SuccessionController : ControllerBase
{
    private readonly SuccessionService _service;

    public SuccessionController(SuccessionService service)
    {
        _service = service;
    }

    [HttpPost("plans")]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<SuccessionPlanResponseDto>> CreatePlan(
        [FromBody] SuccessionPlanRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.CreatePlanAsync(request, cancellationToken).ConfigureAwait(false);
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

    [HttpPost("plans/trigger")]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<SuccessionTriggerResponseDto>> Trigger(
        [FromBody] SuccessionTriggerRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.TriggerAsync(request, cancellationToken).ConfigureAwait(false);
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

    [HttpPost("plans/restore")]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<SuccessionRestoreResponseDto>> Restore(
        [FromBody] SuccessionRestoreRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.RestoreAsync(request, cancellationToken).ConfigureAwait(false);
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
