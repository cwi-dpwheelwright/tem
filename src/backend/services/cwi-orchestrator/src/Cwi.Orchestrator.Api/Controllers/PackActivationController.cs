#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Application.Packs.Activation;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/packs")]
public sealed class PackActivationController : ControllerBase
{
    private readonly PackActivationService _packActivationService;

    public PackActivationController(PackActivationService packActivationService)
    {
        _packActivationService = packActivationService;
    }

    [HttpPost("activation")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<PackActivationResponseDto>> Activate(
        [FromBody] PackActivationRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _packActivationService.ActivateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }

    [HttpPost("rollback")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<PackRollbackResponseDto>> Rollback(
        [FromBody] PackRollbackRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _packActivationService.RollbackAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }
}
#nullable disable
