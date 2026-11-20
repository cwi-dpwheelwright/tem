#nullable enable
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Runtime;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/admin/response-quality-graph")]
public sealed class ResponseQualityGraphController : ControllerBase
{
    private readonly ResponseQualityGraphService _service;

    public ResponseQualityGraphController(ResponseQualityGraphService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ResponseQualityGraphDto>> Get(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (!AdminEntitlementEvaluator.HasAdminEntitlement(User))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Admin entitlement required."));
        }

        var result = await _service.GetAsync(tenantId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
        return Ok(result);
    }
}
#nullable disable
