#nullable enable
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Telemetry;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/admin/sustainability/request-records")]
public sealed class SustainabilityMetricsController : ControllerBase
{
    private readonly SustainabilityMetricsService _service;

    public SustainabilityMetricsController(SustainabilityMetricsService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SustainabilityRequestRecordDto>>> Query(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id_hash")] string? userIdHash,
        [FromQuery(Name = "node_id")] string? nodeId,
        [FromQuery(Name = "start_utc")] DateTimeOffset? startUtc,
        [FromQuery(Name = "end_utc")] DateTimeOffset? endUtc,
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (!AdminEntitlementEvaluator.HasAdminEntitlement(User))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Admin entitlement required."));
        }

        var result = await _service
            .QueryAsync(tenantId, userIdHash, nodeId, startUtc, endUtc, page, pageSize, cancellationToken)
            .ConfigureAwait(false);

        return Ok(result);
    }
}
#nullable disable
