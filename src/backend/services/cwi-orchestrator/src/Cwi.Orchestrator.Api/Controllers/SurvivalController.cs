#nullable enable
using Cwi.Orchestrator.Application.Survival;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/survival")]
public sealed class SurvivalController : ControllerBase
{
    private readonly SurvivalService _service;

    public SurvivalController(SurvivalService service)
    {
        _service = service;
    }

    [HttpPost("signal")]
    public async Task<ActionResult<SurvivalStatusDto>> Signal([FromBody] SurvivalSignalDto signal)
    {
        var result = await _service.RecordAsync(signal).ConfigureAwait(false);
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

    [HttpGet("status")]
    public async Task<ActionResult<SurvivalStatusDto>> Status()
    {
        var result = await _service.GetStatusAsync().ConfigureAwait(false);
        return Ok(result.Data);
    }

    [HttpGet("offline-response")]
    public async Task<ActionResult<OfflineResponseDto>> OfflineResponse()
    {
        var result = await _service.GetOfflineResponseAsync().ConfigureAwait(false);
        return Ok(result.Data);
    }
}
#nullable disable
