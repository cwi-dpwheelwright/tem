using System.Security.Claims;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.HomeNodes;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/ask/deferred")]
public sealed class AskDeferredController : ControllerBase
{
    private readonly HomeNodeDeferredAskService _service;

    public AskDeferredController(HomeNodeDeferredAskService service)
    {
        _service = service;
    }

    [HttpGet("{workId}")]
    public async Task<IActionResult> Get(
        string workId,
        [FromQuery(Name = "request_id")] string requestId,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var error))
        {
            return error;
        }

        var result = await _service.GetAsync(tenantId, userId, requestId, workId, cancellationToken)
            .ConfigureAwait(false);
        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, new
            {
                error = result.Error?.Code ?? ErrorCodes.DependencyUnavailable,
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
