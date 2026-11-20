using System.Security.Claims;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.History;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/history")]
public sealed class AskHistoryController : ControllerBase
{
    private readonly IAskHistoryService _service;

    public AskHistoryController(IAskHistoryService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery(Name = "limit")] int? limit,
        [FromQuery(Name = "retention_days")] int? retentionDays,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var error))
        {
            return error;
        }

        var result = await _service.ListAsync(tenantId, userId, limit, retentionDays, cancellationToken)
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

    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Record([FromBody] AskHistoryRecordRequestDto request, CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var error))
        {
            return error;
        }

        var result = await _service.RecordAsync(tenantId, userId, request, cancellationToken)
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

    [HttpDelete]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var error))
        {
            return error;
        }

        var result = await _service.ClearAsync(tenantId, userId, cancellationToken)
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

    private bool TryGetIdentity(out string tenantId, out string userId, out IActionResult error)
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
}
