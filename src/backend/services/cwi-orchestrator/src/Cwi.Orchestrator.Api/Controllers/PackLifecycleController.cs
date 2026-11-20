#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Application.Packs.Lifecycle;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/packs")]
public sealed class PackLifecycleController : ControllerBase
{
    private readonly PackLifecycleService _packLifecycleService;

    public PackLifecycleController(PackLifecycleService packLifecycleService)
    {
        _packLifecycleService = packLifecycleService;
    }

    [HttpPost("retire")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<PackRetireResponseDto>> Retire(
        [FromBody] PackRetireRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _packLifecycleService.RetireAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }

    [HttpPost("delete")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<PackDeleteResponseDto>> Delete(
        [FromBody] PackDeleteRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _packLifecycleService.DeleteAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }
}
#nullable disable
