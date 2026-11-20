using Cwi.Coordination.Api.Health;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Coordination.Api.Controllers;

[ApiController]
public sealed class ReadyController : ControllerBase
{
    private readonly DependencyHealthChecker _checker;

    public ReadyController(DependencyHealthChecker checker)
    {
        _checker = checker;
    }

    [HttpGet("api/ready")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        var result = await _checker.CheckAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Ready)
        {
            return StatusCode(503, new
            {
                ready = false,
                dependencies = result.Dependencies
            });
        }
        return Ok(new
        {
            ready = true,
            dependencies = result.Dependencies
        });
    }
}
