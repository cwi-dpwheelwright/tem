#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Api.Models;
using Cwi.Orchestrator.Application.Dev;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/dev/fault")]
public sealed class DevFaultController : ControllerBase
{
    private readonly FaultInjectionService _service;

    public DevFaultController(FaultInjectionService service)
    {
        _service = service;
    }

    [HttpPost]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<FaultInjectionResponse>> Inject(
        [FromBody] FaultInjectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled())
        {
            return NotFound(new { error = "fault_injection_disabled" });
        }

        var result = await _service.InjectAsync(request.Kind, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "internal_error",
                message = result.Error?.Message ?? result.Message
            });
        }

        return Ok(new FaultInjectionResponse
        {
            Status = result.Data.Status,
            Detail = result.Data.Detail
        });
    }

    private static bool IsEnabled()
    {
        var value = Environment.GetEnvironmentVariable("CWI_FAULT_INJECTION_ENABLED");
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
#nullable disable
