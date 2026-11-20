using Cwi.Orchestrator.Api.Health;
using Cwi.Orchestrator.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
public sealed class ReadinessController : ControllerBase
{
    private readonly DependencyHealthChecker _checker;

    public ReadinessController(DependencyHealthChecker checker)
    {
        _checker = checker;
    }

    [HttpGet("api/ready")]
    public async Task<ActionResult<ReadyResponse>> Ready(CancellationToken cancellationToken)
    {
        var report = await _checker.CheckAsync(cancellationToken).ConfigureAwait(false);
        var response = new ReadyResponse
        {
            Ready = report.IsHealthy,
            Checks = report.Dependencies.ToDictionary(
                dep => dep.Name,
                dep => new ReadyCheck
                {
                    Status = dep.IsHealthy ? "ok" : "down",
                    Detail = dep.IsHealthy ? null : dep.Detail
                }),
            Timestamp = DateTimeOffset.UtcNow
        };

        return response.Ready ? Ok(response) : StatusCode(503, response);
    }
}
