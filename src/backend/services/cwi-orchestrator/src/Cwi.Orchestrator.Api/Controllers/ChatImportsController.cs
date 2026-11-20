#nullable enable
using System.Security.Claims;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/chat-imports")]
public sealed class ChatImportsController : ControllerBase
{
    private readonly ChatImportService _service;

    public ChatImportsController(ChatImportService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ChatImportSummaryDto>>> List(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId,
        [FromQuery(Name = "limit")] int? limit,
        CancellationToken cancellationToken)
    {
        if (!TryResolveUserId(userId, out var resolvedUserId, out var userError))
        {
            return userError!;
        }

        var result = await _service
            .ListAsync(ResolveTenantId(tenantId) ?? string.Empty, resolvedUserId, limit, cancellationToken)
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

    [HttpGet("{importId}")]
    public async Task<ActionResult<ChatImportDetailDto>> Get(
        string importId,
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId,
        CancellationToken cancellationToken)
    {
        if (!TryResolveUserId(userId, out var resolvedUserId, out var userError))
        {
            return userError!;
        }

        var result = await _service
            .GetAsync(ResolveTenantId(tenantId) ?? string.Empty, importId, resolvedUserId, cancellationToken)
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
