using Cwi.Router.Api.Models;
using Cwi.Router.Application.Health;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Router.Api.Controllers;

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
        var services = new Dictionary<string, string>
        {
            [report.Orchestrator.Name] = report.Orchestrator.IsHealthy ? "ok" : report.Orchestrator.Detail,
            [report.Redis.Name] = report.Redis.IsHealthy ? "ok" : report.Redis.Detail
        };

        var response = new HealthResponse
        {
            Ok = report.IsHealthy,
            Services = services
        };

        return response.Ok ? Ok(response) : StatusCode(503, response);
    }
}
