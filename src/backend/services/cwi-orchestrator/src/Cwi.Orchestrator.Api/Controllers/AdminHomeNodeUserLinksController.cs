#nullable enable
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.HomeNodes;
using Cwi.Orchestrator.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cwi.Orchestrator.Api.Controllers;

[ApiController]
[Route("api/admin/home-node-user-links")]
[JwtRequired]
public sealed class AdminHomeNodeUserLinksController : ControllerBase
{
    private readonly HomeNodeAssignmentService _assignments;
    private readonly HomeNodeRegistryService _nodes;

    public AdminHomeNodeUserLinksController(
        HomeNodeAssignmentService assignments,
        HomeNodeRegistryService nodes)
    {
        _assignments = assignments;
        _nodes = nodes;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AdminHomeNodeUserLinkDto>>> List(
        [FromQuery(Name = "tenant_id")] string? tenantId,
        CancellationToken cancellationToken)
    {
        var requestedTenantId = tenantId?.Trim();
        if (string.IsNullOrWhiteSpace(requestedTenantId))
        {
            return BadRequest(new { error = "invalid_request", message = "tenant_id is required." });
        }

        if (!CanReadTenantLinks())
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                error = "forbidden",
                message = "Administrative access is required to view user home-node links."
            });
        }

        var assignmentResult = await _assignments
            .ListAsync(requestedTenantId, userId: null, cancellationToken)
            .ConfigureAwait(false);
        if (!assignmentResult.IsSuccess || assignmentResult.Data is null)
        {
            return StatusCode(assignmentResult.StatusCode, new
            {
                error = assignmentResult.Error?.Code ?? "invalid_request",
                message = assignmentResult.Error?.Message ?? assignmentResult.Message
            });
        }

        var nodeResult = await _nodes
            .ListAsync(requestedTenantId, ownerUserId: null, status: null, cancellationToken)
            .ConfigureAwait(false);
        if (!nodeResult.IsSuccess || nodeResult.Data is null)
        {
            return StatusCode(nodeResult.StatusCode, new
            {
                error = nodeResult.Error?.Code ?? "invalid_request",
                message = nodeResult.Error?.Message ?? nodeResult.Message
            });
        }

        var nodesById = nodeResult.Data
            .GroupBy(node => node.NodeId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var links = assignmentResult.Data
            .Where(IsActiveAssignment)
            .Select(assignment =>
            {
                nodesById.TryGetValue(assignment.PrimaryHomeNodeId, out var node);
                return new AdminHomeNodeUserLinkDto
                {
                    TenantId = assignment.TenantId,
                    UserId = assignment.UserId,
                    Linked = true,
                    NodeId = assignment.PrimaryHomeNodeId,
                    NodeStatus = node?.Status,
                    TailscaleIp = node?.TailscaleIp,
                    AssignmentStatus = assignment.Status,
                    AssignedUtc = assignment.AssignedUtc,
                    LastHeartbeatUtc = node?.LastHeartbeatUtc,
                    PolicyScopeId = assignment.PolicyScopeId,
                    LocalOnly = assignment.LocalOnly,
                    RemoteFallbackAllowed = assignment.RemoteFallbackAllowed
                };
            })
            .OrderBy(link => link.UserId, StringComparer.Ordinal)
            .ToList();

        return Ok(links);
    }

    private bool CanReadTenantLinks()
    {
        return AdminEntitlementEvaluator.HasAdminEntitlement(User);
    }

    private static bool IsActiveAssignment(HomeNodeAssignmentDto assignment)
    {
        return assignment.RevokedUtc is null
            && string.Equals(assignment.Status, "active", StringComparison.OrdinalIgnoreCase);
    }
}
#nullable disable
