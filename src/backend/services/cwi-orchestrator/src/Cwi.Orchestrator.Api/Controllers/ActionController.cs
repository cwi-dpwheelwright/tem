#nullable enable
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Api.Health;
using Cwi.Orchestrator.Application.Action;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("mcp/action")]
[Route("api/action")]
public sealed class ActionController : ControllerBase
{
    private readonly ActionService _actionService;
    private readonly DependencyHealthChecker _healthChecker;

    public ActionController(ActionService actionService, DependencyHealthChecker healthChecker)
    {
        _actionService = actionService;
        _healthChecker = healthChecker;
    }

    [HttpPost]
    public async Task<ActionResult<ActionResponseDto>> Action(
        [FromBody] ActionRequestDto request,
        CancellationToken cancellationToken)
    {
        var report = await _healthChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!report.IsHealthy)
        {
            return StatusCode(503, new DomainError(ErrorCodes.DependencyUnavailable, "Dependencies unavailable."));
        }

        var result = await _actionService.HandleAsync(request, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }
}
#nullable disable
