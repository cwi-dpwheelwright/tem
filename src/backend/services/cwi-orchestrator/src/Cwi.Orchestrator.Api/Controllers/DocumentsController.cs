#nullable enable
using System.Security.Claims;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Documents;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[JwtRequired]
[Route("api/documents")]
public sealed class DocumentsController : ControllerBase
{
    private readonly DocumentStore _store;

    public DocumentsController(DocumentStore store)
    {
        _store = store;
    }

    [HttpGet]
    public ActionResult<IReadOnlyList<DocumentSummaryDto>> List(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        if (string.IsNullOrWhiteSpace(resolvedTenant))
        {
            return BadRequest(new { error = "invalid_request", message = "tenant_id required" });
        }

        if (!TryResolveUserId(userId, out var resolvedUserId, out var userError))
        {
            return userError!;
        }

        var documents = _store.List(resolvedTenant, resolvedUserId);
        return Ok(documents);
    }

    [HttpGet("{docId}")]
    public ActionResult<DocumentDetailDto> Get(
        string docId,
        [FromQuery(Name = "tenant_id")] string? tenantId,
        [FromQuery(Name = "user_id")] string? userId)
    {
        var resolvedTenant = ResolveTenantId(tenantId);
        if (string.IsNullOrWhiteSpace(resolvedTenant))
        {
            return BadRequest(new { error = "invalid_request", message = "tenant_id required" });
        }

        if (!TryResolveUserId(userId, out var resolvedUserId, out var userError))
        {
            return userError!;
        }

        var document = _store.Get(resolvedTenant, docId, resolvedUserId);
        if (document is null)
        {
            return NotFound(new { error = "not_found", message = "document not found" });
        }

        return Ok(document);
    }

    [HttpPost("{docId}/rollback")]
    [Consumes("application/json")]
    public ActionResult<DocumentRollbackResponseDto> Rollback(
        string docId,
        [FromBody] DocumentRollbackRequestDto request,
        [FromQuery(Name = "tenant_id")] string? tenantId)
    {
        if (request is null)
        {
            return BadRequest(new { error = "invalid_request", message = "request body required" });
        }

        var resolvedTenant = ResolveTenantId(request.TenantId) ?? ResolveTenantId(tenantId);
        if (string.IsNullOrWhiteSpace(resolvedTenant))
        {
            return BadRequest(new { error = "invalid_request", message = "tenant_id required" });
        }

        if (string.IsNullOrWhiteSpace(request.ActorId))
        {
            return BadRequest(new { error = "invalid_request", message = "actor_id required" });
        }

        if (string.IsNullOrWhiteSpace(request.TargetVersion))
        {
            return BadRequest(new { error = "invalid_request", message = "target_version required" });
        }

        if (!TryResolveUserId(null, out var resolvedUserId, out var userError))
        {
            return userError!;
        }

        var normalized = new DocumentRollbackRequestDto
        {
            TenantId = resolvedTenant,
            ActorId = request.ActorId,
            TargetVersion = request.TargetVersion,
            Reason = request.Reason
        };

        var response = _store.Rollback(resolvedTenant, docId, resolvedUserId, normalized);
        if (response is null)
        {
            return NotFound(new { error = "not_found", message = "document not found" });
        }

        if (string.Equals(response.Status, "not_found", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(response);
        }

        return Ok(response);
    }

    private string? ResolveTenantId(string? tenantId)
    {
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

        if (!string.IsNullOrWhiteSpace(claimUserId) &&
            !string.IsNullOrWhiteSpace(trimmedRequestedUserId) &&
            !string.Equals(claimUserId, trimmedRequestedUserId, StringComparison.OrdinalIgnoreCase))
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
