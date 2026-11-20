using System.Collections.Generic;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Runtime;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/admin/runtime-capabilities")]
[JwtRequired]
public sealed class RuntimeCapabilityStatusController : ControllerBase
{
    private readonly RuntimeCapabilityStatusService _service;

    public RuntimeCapabilityStatusController(RuntimeCapabilityStatusService service)
    {
        _service = service;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<RuntimeCapabilityStatusDto>> List()
    {
        if (!AdminEntitlementEvaluator.HasAdminEntitlement(User))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "forbidden",
                message = "Administrative access is required to view runtime capability status."
            });
        }

        return Ok(_service.GetMatrix());
    }
}
