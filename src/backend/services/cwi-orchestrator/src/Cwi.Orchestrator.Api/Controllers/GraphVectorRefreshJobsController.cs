#nullable enable
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/admin/graph-vector-refresh-jobs")]
public sealed class GraphVectorRefreshJobsController : ControllerBase
{
    private readonly GraphToVectorRefreshService _service;

    public GraphVectorRefreshJobsController(GraphToVectorRefreshService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GraphVectorRefreshJobDto>>> Query(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "refresh_status")] string? refreshStatus,
        [FromQuery(Name = "graph_candidate_id")] string? graphCandidateId,
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (!AdminEntitlementEvaluator.HasAdminEntitlement(User))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Admin entitlement required."));
        }

        var result = await _service
            .QueryAsync(tenantId, refreshStatus, graphCandidateId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);

        return Ok(result);
    }

    [HttpPost("process-pending")]
    public async Task<ActionResult<GraphVectorRefreshProcessResultDto>> ProcessPending(
        [FromBody] GraphVectorRefreshProcessRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!AdminEntitlementEvaluator.HasAdminEntitlement(User))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Admin entitlement required."));
        }

        var result = await _service
            .ProcessPendingAsync(request.TenantId, request.PageSize, cancellationToken)
            .ConfigureAwait(false);

        return Ok(result);
    }
}
#nullable disable
