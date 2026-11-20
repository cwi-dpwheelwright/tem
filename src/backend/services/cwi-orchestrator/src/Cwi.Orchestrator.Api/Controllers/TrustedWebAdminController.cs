#nullable enable
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.TrustedWeb;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/admin/trusted-web")]
[JwtRequired]
public sealed class TrustedWebAdminController : ControllerBase
{
    private readonly TrustedWebConfigService _service;

    public TrustedWebAdminController(TrustedWebConfigService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<TrustedWebConfigDto>> Get(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        CancellationToken cancellationToken)
    {
        var result = await _service.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, new { error = result.Error?.Code ?? "invalid_request", message = result.Message });
        }

        return Ok(result.Data);
    }

    [HttpPut]
    public async Task<ActionResult<TrustedWebConfigDto>> Save(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromBody] TrustedWebConfigDto? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "invalid_request", message = "trusted web config body required" });
        }

        var actor = ResolveActorUserId();
        var result = await _service.SaveAsync(tenantId, request, actor, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, new { error = result.Error?.Code ?? "invalid_request", message = result.Message });
        }

        return Ok(result.Data);
    }

    private string? ResolveActorUserId()
    {
        return User.FindFirst("sub")?.Value
            ?? User.FindFirst("user_id")?.Value
            ?? User.Identity?.Name;
    }
}
#nullable disable
