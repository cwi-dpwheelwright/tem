#nullable enable
using Cwi.Orchestrator.Api.BreakGlass;
using Cwi.Orchestrator.Api.Models;
using Cwi.Orchestrator.Application.BreakGlass;
using Cwi.Orchestrator.Application.Policy;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/admin/kill_switch")]
public sealed class KillSwitchAdminController : ControllerBase
{
    private readonly KillSwitchAdminService _service;
    private readonly KillSwitchAdminOptions _options;

    public KillSwitchAdminController(KillSwitchAdminService service, KillSwitchAdminOptions options)
    {
        _service = service;
        _options = options;
    }

    [HttpGet]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<KillSwitchStatusResponse>> Get(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return NotFound(new { error = "kill_switch_admin_disabled", message = "Kill switch admin endpoints are disabled." });
        }

        var resolvedTenantId = ResolveTenantId(tenantId);
        if (string.IsNullOrWhiteSpace(resolvedTenantId))
        {
            return BadRequest(new { error = "invalid_request", message = "tenant_id required" });
        }

        var result = await _service.GetAsync(resolvedTenantId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, new { error = result.Error?.Code ?? "invalid_request", message = result.Message });
        }

        return Ok(ToResponse(result.Data));
    }

    [HttpPost]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<KillSwitchStatusResponse>> Toggle(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromBody] KillSwitchToggleRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return NotFound(new { error = "kill_switch_admin_disabled", message = "Kill switch admin endpoints are disabled." });
        }

        var resolvedTenantId = ResolveTenantId(tenantId);
        if (string.IsNullOrWhiteSpace(resolvedTenantId))
        {
            return BadRequest(new { error = "invalid_request", message = "tenant_id required" });
        }

        var result = await _service
            .SetAsync(
                resolvedTenantId,
                request.Enabled,
                request.Irreversible,
                request.KillCode,
                request.TokenId,
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, new { error = result.Error?.Code ?? "invalid_request", message = result.Message });
        }

        return Ok(ToResponse(result.Data));
    }

    [HttpPost("token")]
    [BreakGlassScope("admin")]
    public async Task<ActionResult<KillSwitchTokenResponse>> IssueToken(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromBody] KillSwitchTokenRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return NotFound(new { error = "kill_switch_admin_disabled", message = "Kill switch admin endpoints are disabled." });
        }

        var resolvedTenantId = ResolveTenantId(tenantId);
        if (string.IsNullOrWhiteSpace(resolvedTenantId))
        {
            return BadRequest(new { error = "invalid_request", message = "tenant_id required" });
        }

        var result = await _service
            .IssueTokenAsync(resolvedTenantId, request.KillCode, request.TtlSeconds, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, new { error = result.Error?.Code ?? "invalid_request", message = result.Message });
        }

        return Ok(new KillSwitchTokenResponse
        {
            TokenId = result.Data.TokenId,
            ExpiresUtc = result.Data.ExpiresUtc.ToString("O")
        });
    }

    private static KillSwitchStatusResponse ToResponse(KillSwitchStatus status)
    {
        return new KillSwitchStatusResponse
        {
            Enabled = status.Enabled,
            Irreversible = status.Irreversible,
            UpdatedUtc = status.UpdatedUtc == DateTimeOffset.MinValue ? null : status.UpdatedUtc.ToString("O")
        };
    }

    private string? ResolveTenantId(string? tenantId)
    {
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            return tenantId.Trim();
        }

        if (HttpContext.Items.TryGetValue("break_glass_token", out var tokenObj) &&
            tokenObj is BreakGlassTokenInfo tokenInfo &&
            !string.IsNullOrWhiteSpace(tokenInfo.TenantId))
        {
            return tokenInfo.TenantId.Trim();
        }

        var headerTenant = Request.Headers["x-tenant-id"].ToString();
        if (!string.IsNullOrWhiteSpace(headerTenant))
        {
            return headerTenant.Trim();
        }

        var envTenant = Environment.GetEnvironmentVariable("DEFAULT_TENANT_ID");
        if (!string.IsNullOrWhiteSpace(envTenant))
        {
            return envTenant.Trim();
        }

        return null;
    }
}
#nullable disable
