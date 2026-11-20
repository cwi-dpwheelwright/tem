#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Application.Packs.Lifecycle;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/packs/audit")]
public sealed class PackAuditController : ControllerBase
{
    private readonly PackAuditService _auditService;

    public PackAuditController(PackAuditService auditService)
    {
        _auditService = auditService;
    }

    [HttpGet]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<PackAuditResponseDto>> Get(
        [FromQuery(Name = "start_utc")] DateTimeOffset? startUtc,
        [FromQuery(Name = "end_utc")] DateTimeOffset? endUtc,
        [FromQuery(Name = "pack_id")] string? packId,
        [FromQuery(Name = "actor_id")] string? actorUserId,
        [FromQuery(Name = "event_type")] string? eventType,
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "tenant_id")] string? tenantId,
        CancellationToken cancellationToken)
    {
        var resolvedTenant = ResolveTenantId(tenantId);

        var request = new PackAuditQuery
        {
            StartUtc = startUtc,
            EndUtc = endUtc,
            PackId = packId,
            ActorUserId = actorUserId,
            EventType = eventType,
            TenantId = resolvedTenant,
            Limit = limit ?? 100
        };

        var result = await _auditService.QueryAsync(request, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
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
