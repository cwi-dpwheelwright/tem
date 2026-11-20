#nullable enable
using System.Security.Claims;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Connectors;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/connectors/webhook")]
public sealed class ConnectorWebhooksController : ControllerBase
{
    private readonly ConnectorWebhookIngestionService _service;

    public ConnectorWebhooksController(ConnectorWebhookIngestionService service)
    {
        _service = service;
    }

    [HttpPost("{type}")]
    [Consumes("application/json")]
    public async Task<ActionResult<ConnectorWebhookResponseDto>> Ingest(
        string type,
        [FromBody] ConnectorWebhookRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryResolveIdentity(request, out var tenantId, out var userId, out var error))
        {
            return error!;
        }

        var result = await _service.IngestAsync(type, tenantId, userId, request, cancellationToken).ConfigureAwait(false);
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

    private bool TryResolveIdentity(
        ConnectorWebhookRequestDto request,
        out string tenantId,
        out string? userId,
        out ActionResult? error)
    {
        var claimTenantId = User.FindFirst("tenant_id")?.Value?.Trim();
        var claimUserId = User.FindFirst("sub")?.Value?.Trim()
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim();
        var requestTenantId = request.TenantId?.Trim();
        var requestUserId = request.UserId?.Trim();

        if (!string.IsNullOrWhiteSpace(claimUserId)
            && !string.IsNullOrWhiteSpace(requestUserId)
            && !string.Equals(claimUserId, requestUserId, StringComparison.OrdinalIgnoreCase))
        {
            tenantId = string.Empty;
            userId = null;
            error = StatusCode(403, new DomainError(ErrorCodes.Forbidden, "user_id does not match the authenticated session."));
            return false;
        }

        tenantId = !string.IsNullOrWhiteSpace(claimTenantId) ? claimTenantId : requestTenantId ?? string.Empty;
        userId = !string.IsNullOrWhiteSpace(claimUserId) ? claimUserId : requestUserId;
        error = null;
        return true;
    }
}
#nullable disable
