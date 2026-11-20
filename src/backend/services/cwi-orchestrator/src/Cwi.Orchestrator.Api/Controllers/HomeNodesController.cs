#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Application.HomeNodes;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/home-nodes")]
public sealed class HomeNodesController : ControllerBase
{
    private readonly HomeNodeRegistryService _service;

    public HomeNodesController(HomeNodeRegistryService service)
    {
        _service = service;
    }

    [HttpPost]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<HomeNodeDto>> Upsert(
        [FromBody] HomeNodeUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.UpsertAsync(request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpGet]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<IReadOnlyList<HomeNodeDto>>> List(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "owner_user_id")] string? ownerUserId,
        [FromQuery(Name = "status")] string? status,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        var result = await _service
            .ListAsync(resolvedTenant ?? string.Empty, ownerUserId, status, cancellationToken)
            .ConfigureAwait(false);

        return ToActionResult(result);
    }

    [HttpGet("{nodeId}")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<HomeNodeDto>> Get(
        string nodeId,
        [FromQuery(Name = "tenant_id")] string? tenantId,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        var result = await _service
            .GetAsync(resolvedTenant ?? string.Empty, nodeId, cancellationToken)
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
