#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Application.HomeNodes;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/home-node-assignments")]
public sealed class HomeNodeAssignmentsController : ControllerBase
{
    private readonly HomeNodeAssignmentService _service;

    public HomeNodeAssignmentsController(HomeNodeAssignmentService service)
    {
        _service = service;
    }

    [HttpPost]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<HomeNodeAssignmentDto>> Upsert(
        [FromBody] HomeNodeAssignmentUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpsertAsync(request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpGet]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<IReadOnlyList<HomeNodeAssignmentDto>>> List(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        var result = await _service
            .ListAsync(resolvedTenant ?? string.Empty, userId, cancellationToken)
            .ConfigureAwait(false);

        return ToActionResult(result);
    }

    [HttpGet("{userId}")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<HomeNodeAssignmentDto>> Get(
        string userId,
        [FromQuery(Name = "tenant_id")] string? tenantId,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        var result = await _service
            .GetAsync(resolvedTenant ?? string.Empty, userId, cancellationToken)
            .ConfigureAwait(false);

        return ToActionResult(result);
    }

    private ActionResult<T> ToActionResult<T>(Cwi.Common.ServiceResponse<T> result)
    {
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "invalid_request",
                message = result.Error?.Message ?? result.Message
            });
        }

        return Ok(result.Data);
    }

    private string? ResolveTenantId(string? tenantId)
    {
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            return tenantId.Trim();
        }

        var header = Request.Headers["x-tenant-id"].ToString();
        return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
    }
}
#nullable disable
