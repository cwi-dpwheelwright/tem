#nullable enable
using System.Security.Claims;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.HomeNodes;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/account/home-node")]
public sealed class AccountHomeNodeController : ControllerBase
{
    private readonly CurrentUserHomeNodeLinkService _service;

    public AccountHomeNodeController(CurrentUserHomeNodeLinkService service)
    {
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<CurrentUserHomeNodeLinkDto>> Get(CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var error))
        {
            return error;
        }

        var result = await _service.GetAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<CurrentUserHomeNodeLinkDto>> Link(
        [FromBody] CurrentUserHomeNodeLinkRequestDto request,
        CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var error))
        {
            return error;
        }

        var result = await _service.LinkAsync(tenantId, userId, request, cancellationToken).ConfigureAwait(false);
        return ToActionResult(result);
    }

    [HttpDelete]
    public async Task<ActionResult<CurrentUserHomeNodeLinkDto>> Unlink(CancellationToken cancellationToken)
    {
        if (!TryGetIdentity(out var tenantId, out var userId, out var error))
        {
            return error;
        }

        var result = await _service.UnlinkAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
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

        return Ok(result.Data);
    }
}
#nullable disable
