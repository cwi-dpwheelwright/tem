#nullable enable
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/admin/graph-update-candidates")]
public sealed class GraphUpdateCandidatesController : ControllerBase
{
    private readonly GraphEvolutionService _service;

    public GraphUpdateCandidatesController(GraphEvolutionService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GraphUpdateCandidateDto>>> Query(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "candidate_state")] string? candidateState,
        [FromQuery(Name = "temporal_state")] string? temporalState,
        [FromQuery(Name = "page")] int page = 1,
        [FromQuery(Name = "page_size")] int pageSize = 100,
        CancellationToken cancellationToken = default)
    {
        if (!AdminEntitlementEvaluator.HasAdminEntitlement(User))
        {
            return StatusCode(403, new DomainError(ErrorCodes.Forbidden, "Admin entitlement required."));
        }

        var result = await _service
            .QueryAsync(tenantId, candidateState, temporalState, page, pageSize, cancellationToken)
            .ConfigureAwait(false);

        return Ok(result);
    }
}
#nullable disable
