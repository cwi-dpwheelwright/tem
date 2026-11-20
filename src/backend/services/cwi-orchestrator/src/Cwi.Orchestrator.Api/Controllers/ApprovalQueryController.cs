#nullable enable
using Cwi.Orchestrator.Application.Action;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
public sealed class ApprovalQueryController : ControllerBase
{
    private const string RolesHeader = "x-roles";
    private const string TenantHeader = "x-tenant-id";
    private static readonly string[] AllowedRoles = { "approver", "admin" };

    private readonly ApprovalQueryService _approvalQueryService;

    public ApprovalQueryController(ApprovalQueryService approvalQueryService)
    {
        _approvalQueryService = approvalQueryService;
    }

    [HttpGet("mcp/approval/{ticketId}")]
    [HttpGet("api/approval/{ticketId}")]
    public async Task<ActionResult<ApprovalTicketStatusDto>> GetStatus(
        [FromRoute] string ticketId,
        CancellationToken cancellationToken)
    {
        if (!HasApprovalReadAccess(out var tenantId))
        {
            return StatusCode(403, "Forbidden");
        }

        var result = await _approvalQueryService.GetStatusAsync(ticketId, tenantId, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }

    [HttpGet("mcp/approval")]
    [HttpGet("api/approvals")]
    public async Task<ActionResult<IReadOnlyList<ApprovalTicketStatusDto>>> List(
        [FromQuery(Name = "tenant_id")] string tenantId,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        if (!HasApprovalReadAccess(out var tenantHeader))
        {
            return StatusCode(403, "Forbidden");
        }

        if (!string.IsNullOrWhiteSpace(tenantHeader) &&
            !string.Equals(tenantHeader, tenantId, StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(403, "Forbidden");
        }

        var result = await _approvalQueryService.ListAsync(tenantId, limit, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data is null)
        {
            return StatusCode(result.StatusCode, result.Error);
        }

        return Ok(result.Data);
    }

    private bool HasApprovalReadAccess(out string tenantId)
    {
        tenantId = Request.Headers[TenantHeader].ToString();
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return false;
        }

        var roles = Request.Headers[RolesHeader].ToString();
        if (string.IsNullOrWhiteSpace(roles))
        {
            return false;
        }

        return roles
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(role => AllowedRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
    }
}
#nullable disable
