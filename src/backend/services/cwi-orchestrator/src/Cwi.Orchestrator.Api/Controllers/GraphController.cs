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
[Route("api/graph")]
public sealed class GraphController : ControllerBase
{
    private readonly GraphQueryService _graphQueryService;
    private readonly UserGraphProfileService _profileService;

    public GraphController(
        GraphQueryService graphQueryService,
        UserGraphProfileService profileService)
    {
        _graphQueryService = graphQueryService;
        _profileService = profileService;
    }

    [HttpPost("query")]
    public async Task<ActionResult<GraphQueryResponseDto>> Query(
        [FromBody] GraphQueryRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveUserId(request.UserId, out var resolvedUserId, out var userError))
        {
            return userError!;
        }

        var normalized = new GraphQueryRequestDto
        {
            TenantId = ResolveTenantId(request.TenantId),
            UserId = resolvedUserId,
            Question = request.Question,
            TopK = request.TopK
        };

        var result = await _graphQueryService.QueryAsync(normalized, cancellationToken).ConfigureAwait(false);
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

    [HttpGet("profile")]
    public async Task<ActionResult<UserGraphProfileResponseDto>> Profile(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId,
        [FromQuery(Name = "top_k")] int? topK,
        CancellationToken cancellationToken)
    {
        if (!TryResolveUserId(userId, out var resolvedUserId, out var userError))
        {
            return userError!;
        }

        var result = await _profileService
            .GetAsync(ResolveTenantId(tenantId) ?? string.Empty, resolvedUserId, topK.GetValueOrDefault(5), cancellationToken)
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
        var claimTenantId = User.FindFirst("tenant_id")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(claimTenantId))
        {
            return claimTenantId;
        }

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            return tenantId.Trim();
        }

        var header = Request.Headers["x-tenant-id"].ToString();
        return string.IsNullOrWhiteSpace(header) ? null : header.Trim();
    }

    private bool TryResolveUserId(string? requestedUserId, out string? resolvedUserId, out ActionResult? error)
    {
        var claimUserId = User.FindFirst("sub")?.Value?.Trim()
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
        var trimmedRequestedUserId = string.IsNullOrWhiteSpace(requestedUserId) ? null : requestedUserId.Trim();

        if (!string.IsNullOrWhiteSpace(claimUserId)
            && !string.IsNullOrWhiteSpace(trimmedRequestedUserId)
            && !string.Equals(claimUserId, trimmedRequestedUserId, StringComparison.OrdinalIgnoreCase))
        {
            resolvedUserId = null;
            error = StatusCode(403, new DomainError(ErrorCodes.Forbidden, "user_id does not match the authenticated session."));
            return false;
        }

        resolvedUserId = !string.IsNullOrWhiteSpace(claimUserId) ? claimUserId : trimmedRequestedUserId;
        error = null;
        return true;
    }
}
#nullable disable
