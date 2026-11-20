#nullable enable
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Ownership;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/ownership")]
public sealed class OwnershipGraphController : ControllerBase
{
    private readonly OwnershipGraphService _service;

    public OwnershipGraphController(OwnershipGraphService service)
    {
        _service = service;
    }

    [HttpGet("lineage")]
    public async Task<ActionResult<OwnershipLineageResponseDto>> Lineage(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        var result = await _service.GetLineageAsync(resolvedTenant ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? "invalid_request",
                message = result.Error?.Message ?? result.Message
            });
        }

        return Ok(result.Data);
    }

    [HttpGet("revocations")]
    public async Task<ActionResult<IReadOnlyList<OwnershipRevocationEntryDto>>> Revocations(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        var result = await _service.GetRevocationsAsync(resolvedTenant ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess)
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
