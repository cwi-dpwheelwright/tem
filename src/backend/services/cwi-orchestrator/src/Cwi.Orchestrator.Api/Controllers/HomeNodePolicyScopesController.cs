#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Application.HomeNodes;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/home-node-policy-scopes")]
public sealed class HomeNodePolicyScopesController : ControllerBase
{
    private readonly HomeNodePolicyScopeService _service;

    public HomeNodePolicyScopesController(HomeNodePolicyScopeService service)
    {
        _service = service;
    }

    [HttpPost]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<HomeNodePolicyScopeDto>> Upsert(
        [FromBody] HomeNodePolicyScopeUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpsertAsync(request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpGet]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<IReadOnlyList<HomeNodePolicyScopeDto>>> List(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        var result = await _service.ListAsync(resolvedTenant ?? string.Empty, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpGet("{policyScopeId}")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<HomeNodePolicyScopeDto>> Get(
        string policyScopeId,
        [FromQuery(Name = "tenant_id")] string? tenantId,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        var result = await _service
            .GetAsync(resolvedTenant ?? string.Empty, policyScopeId, cancellationToken)
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
