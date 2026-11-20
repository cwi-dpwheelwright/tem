#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/break-glass/protected")]
[BreakGlassScope("admin")]
public sealed class BreakGlassProtectedController : ControllerBase
{
    [HttpPost]
    // nosemgrep: csharp.dotnet.security.mvc-missing-antiforgery
    public IActionResult Post()
    {
        return Ok(new { ok = true });
    }
}
#nullable disable
