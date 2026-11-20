#nullable enable
using System.Security.Claims;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/user")]
public sealed class UserIntelligenceController : ControllerBase
{
    private readonly UserIntelligenceService _service;

    public UserIntelligenceController(UserIntelligenceService service)
    {
        _service = service;
    }

    [HttpGet("profile")]
    public async Task<ActionResult<UserGraphProfileResponseDto>> Profile(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId,
        [FromQuery(Name = "top_k")] int? topK,
        CancellationToken cancellationToken)
    {
        if (!TryResolveIdentity(tenantId, userId, out var resolvedTenantId, out var resolvedUserId, out var error))
        {
            return error!;
        }

        var result = await _service
            .GetProfileAsync(resolvedTenantId, resolvedUserId, topK.GetValueOrDefault(5), cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpGet("summary/daily")]
    public Task<ActionResult<UserIntelligenceSummaryDto>> DailySummary(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId,
        [FromQuery(Name = "top_k")] int? topK,
        CancellationToken cancellationToken)
    {
        return Summary("daily", tenantId, userId, topK, cancellationToken);
    }

    [HttpGet("summary/weekly")]
    public Task<ActionResult<UserIntelligenceSummaryDto>> WeeklySummary(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId,
        [FromQuery(Name = "top_k")] int? topK,
        CancellationToken cancellationToken)
    {
        return Summary("weekly", tenantId, userId, topK, cancellationToken);
    }

    [HttpGet("suggestions")]
    public async Task<ActionResult<UserSuggestionListDto>> Suggestions(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId,
        [FromQuery(Name = "top_k")] int? topK,
        CancellationToken cancellationToken)
    {
        if (!TryResolveIdentity(tenantId, userId, out var resolvedTenantId, out var resolvedUserId, out var error))
        {
            return error!;
        }

        var result = await _service
            .GetSuggestionsAsync(resolvedTenantId, resolvedUserId, topK.GetValueOrDefault(8), cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpGet("briefing")]
    public async Task<ActionResult<UserBriefingDto>> Briefing(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId,
        [FromQuery(Name = "top_k")] int? topK,
        CancellationToken cancellationToken)
    {
        if (!TryResolveIdentity(tenantId, userId, out var resolvedTenantId, out var resolvedUserId, out var error))
        {
            return error!;
        }

        var result = await _service
            .GetBriefingAsync(resolvedTenantId, resolvedUserId, topK.GetValueOrDefault(8), cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpGet("changes")]
    public async Task<ActionResult<UserChangeListDto>> Changes(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId,
        [FromQuery(Name = "top_k")] int? topK,
        CancellationToken cancellationToken)
    {
        if (!TryResolveIdentity(tenantId, userId, out var resolvedTenantId, out var resolvedUserId, out var error))
        {
            return error!;
        }

        var result = await _service
            .GetChangesAsync(resolvedTenantId, resolvedUserId, topK.GetValueOrDefault(8), cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpGet("risk")]
    public async Task<ActionResult<UserRiskListDto>> Risk(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId,
        [FromQuery(Name = "top_k")] int? topK,
        CancellationToken cancellationToken)
    {
        if (!TryResolveIdentity(tenantId, userId, out var resolvedTenantId, out var resolvedUserId, out var error))
        {
            return error!;
        }

        var result = await _service
            .GetRisksAsync(resolvedTenantId, resolvedUserId, topK.GetValueOrDefault(8), cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpGet("plan")]
    public async Task<ActionResult<UserPlanDto>> Plan(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId,
        [FromQuery(Name = "top_k")] int? topK,
        CancellationToken cancellationToken)
    {
        if (!TryResolveIdentity(tenantId, userId, out var resolvedTenantId, out var resolvedUserId, out var error))
        {
            return error!;
        }

        var result = await _service
            .GetPlanAsync(resolvedTenantId, resolvedUserId, topK.GetValueOrDefault(8), cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }

    private async Task<ActionResult<UserIntelligenceSummaryDto>> Summary(
        string period,
        string? tenantId,
        string? userId,
        int? topK,
        CancellationToken cancellationToken)
    {
        if (!TryResolveIdentity(tenantId, userId, out var resolvedTenantId, out var resolvedUserId, out var error))
        {
            return error!;
        }

        var result = await _service
            .GetSummaryAsync(resolvedTenantId, resolvedUserId, period, topK.GetValueOrDefault(5), cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }

    private bool TryResolveIdentity(
        string? tenantId,
        string? userId,
        out string resolvedTenantId,
        out string? resolvedUserId,
        out ActionResult? error)
    {
        var claimTenantId = User.FindFirst("tenant_id")?.Value?.Trim();
        var claimUserId = User.FindFirst("sub")?.Value?.Trim()
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
        var requestedUserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();

        if (!string.IsNullOrWhiteSpace(claimUserId)
            && !string.IsNullOrWhiteSpace(requestedUserId)
            && !string.Equals(claimUserId, requestedUserId, StringComparison.OrdinalIgnoreCase))
        {
            resolvedTenantId = string.Empty;
            resolvedUserId = null;
            error = StatusCode(403, new DomainError(ErrorCodes.Forbidden, "user_id does not match the authenticated session."));
            return false;
        }

        resolvedTenantId = !string.IsNullOrWhiteSpace(claimTenantId)
            ? claimTenantId
            : tenantId?.Trim() ?? string.Empty;
        resolvedUserId = !string.IsNullOrWhiteSpace(claimUserId) ? claimUserId : requestedUserId;
        error = null;
        return true;
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

        return StatusCode(result.StatusCode, result.Data);
    }
}
#nullable disable
