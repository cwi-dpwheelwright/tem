#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Application.Retention;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/admin/retention")]
public sealed class RetentionPolicyController : ControllerBase
{
    private readonly RetentionPolicyOptions _options;

    public RetentionPolicyController(RetentionPolicyOptions options)
    {
        _options = options;
    }

    [HttpGet]
    [BreakGlassScope("admin")]
    public ActionResult<RetentionPolicyOptions> Get()
    {
        return Ok(_options);
    }
}
#nullable disable
