using Cwi.Orchestrator.Api.Health;
using Cwi.Orchestrator.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    private readonly DependencyHealthChecker _checker;

    public HealthController(DependencyHealthChecker checker)
    {
        _checker = checker;
    }

    [HttpGet]
    public async Task<ActionResult<HealthResponse>> Get(CancellationToken cancellationToken)
    {
        var report = await _checker.CheckAsync(cancellationToken).ConfigureAwait(false);

        var response = new HealthResponse
        {
            Ok = report.IsHealthy,
            Services = report.Dependencies.ToDictionary(
                dep => dep.Name,
                dep => dep.IsHealthy ? "ok" : dep.Detail ?? "down")
        };

        return response.Ok ? Ok(response) : StatusCode(503, response);
    }
}
