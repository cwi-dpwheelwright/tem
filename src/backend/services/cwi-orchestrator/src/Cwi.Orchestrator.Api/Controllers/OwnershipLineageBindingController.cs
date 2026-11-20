#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Ownership;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/ownership/lineage")]
public sealed class OwnershipLineageBindingController : ControllerBase
{
    private readonly OwnershipLineageBindingService _service;

    public OwnershipLineageBindingController(OwnershipLineageBindingService service)
    {
        _service = service;
    }

    [HttpPost("bind")]
    [Consumes("application/json")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<OwnershipLineageBindingResponseDto>> Bind(
        [FromBody] OwnershipLineageBindingRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await _service.BindAsync(request, cancellationToken).ConfigureAwait(false);
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

    [HttpGet("assets")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<OwnershipLineageResponseDto>> Assets(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        var result = await _service
            .GetAssetLineageAsync(resolvedTenant ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

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
