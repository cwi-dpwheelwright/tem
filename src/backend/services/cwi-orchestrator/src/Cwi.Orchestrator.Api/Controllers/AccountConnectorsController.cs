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
[Route("api/account/connectors")]
public sealed class AccountConnectorsController : ControllerBase
{
    private readonly AccountConnectorService _service;

    public AccountConnectorsController(AccountConnectorService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<AccountConnectorListDto>> List(CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var error))
        {
            return error;
        }

        var result = await _service.ListAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<AccountConnectorConnectionDto>> Create(
        [FromBody] AccountConnectorCreateRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var error))
        {
            return error;
        }

        var result = await _service.CreateAsync(tenantId, userId, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpPatch("{connectionId}")]
    [Consumes("application/json")]
    public async Task<ActionResult<AccountConnectorConnectionDto>> Update(
        string connectionId,
        [FromBody] AccountConnectorUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var error))
        {
            return error;
        }

        var result = await _service.UpdateAsync(tenantId, userId, connectionId, request, cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpDelete("{connectionId}")]
    public async Task<ActionResult<AccountConnectorConnectionDto>> Disconnect(
        string connectionId,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var error))
        {
            return error;
        }

        var result = await _service.DisconnectAsync(tenantId, userId, connectionId, cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpPost("{connectionId}/sync")]
    [Consumes("application/json")]
    public async Task<ActionResult<AccountConnectorConnectionDto>> QueueSync(
        string connectionId,
        [FromBody] AccountConnectorSyncRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var error))
        {
            return error;
        }

        var result = await _service.QueueSyncAsync(tenantId, userId, connectionId, request, cancellationToken)
            .ConfigureAwait(false);
        return ToActionResult(result);
    }

    private bool TryGetIdentity(out string tenantId, out string userId, out ActionResult error)
    {
        tenantId = User.FindFirst("tenant_id")?.Value?.Trim() ?? string.Empty;
        userId = User.FindFirst("sub")?.Value?.Trim()
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value?.Trim()
            ?? string.Empty;

        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId))
        {
            error = StatusCode(401, new DomainError(ErrorCodes.Unauthorized, "Missing required token claims."));
            return false;
        }

        error = null!;
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
