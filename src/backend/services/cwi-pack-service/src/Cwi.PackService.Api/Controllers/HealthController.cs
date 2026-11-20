using Cwi.PackService.Domain.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.PackService.Api.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    private readonly IPackRepository _repository;

    public HealthController(IPackRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("/healthz")]
    public IActionResult Healthz() => Ok(new { status = "ok" });

    [HttpGet("/readyz")]
    public IActionResult Readyz()
    {
        var canAccess = _repository.CanAccessDirectory();
        var payload = new
        {
            status = canAccess ? "ready" : "down",
            checks = new
            {
                packs_dir = canAccess ? "ok" : "down"
            }
        };

        return canAccess ? Ok(payload) : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
    }
}
