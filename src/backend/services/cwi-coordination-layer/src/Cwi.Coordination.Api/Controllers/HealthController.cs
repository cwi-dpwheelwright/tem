using Cwi.Coordination.Api.Health;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    private readonly DependencyHealthChecker _checker;

    public HealthController(DependencyHealthChecker checker)
    {
        _checker = checker;
    }

    [HttpGet("health")]
    [HttpGet("api/health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken)
    {
        var result = await _checker.CheckAsync(cancellationToken).ConfigureAwait(false);
        var response = new
        {
            ok = result.Ready,
            dependencies = result.Dependencies
        };

        return result.Ready ? Ok(response) : StatusCode(503, response);
    }
}
