#nullable enable
using System.Linq;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Ownership;

public sealed class OwnershipGraphService : DomainServiceBase
{
    private readonly IOwnershipTransferRepository _repository;

    public OwnershipGraphService(IOwnershipTransferRepository repository)
    {
        _repository = repository;
    }

    public Task<ServiceResponse<OwnershipLineageResponseDto>> GetLineageAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return ServiceResponse<OwnershipLineageResponseDto>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                    statusCode: 400,
                    message: "tenant_id required");
            }

            var records = await _repository.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var nodes = new Dictionary<string, OwnershipLineageNodeDto>(StringComparer.OrdinalIgnoreCase);
            var edges = new List<OwnershipLineageEdgeDto>();

            foreach (var record in records)
            {
                var relationship = MapRelationship(record.TransferType);
                var scope = ExtractScope(record.Reason);
                edges.Add(new OwnershipLineageEdgeDto
                {
                    From = record.FromUserId,
                    To = record.ToUserId,
                    Scope = scope,
                    Relationship = relationship
                });

                UpsertNode(nodes, record.FromUserId, "Owner", "active");
                var toRole = relationship == "successor" ? "Successor" : "Delegate";
                var toStatus = MapNodeStatus(record.Status);
                UpsertNode(nodes, record.ToUserId, toRole, toStatus);
            }

            return ServiceResponse<OwnershipLineageResponseDto>.Success(new OwnershipLineageResponseDto
            {
                Nodes = nodes.Values.OrderBy(node => node.Label).ToList(),
                Edges = edges
            });
        });
    }

    public Task<ServiceResponse<IReadOnlyList<OwnershipRevocationEntryDto>>> GetRevocationsAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return ServiceResponse<IReadOnlyList<OwnershipRevocationEntryDto>>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                    statusCode: 400,
                    message: "tenant_id required");
            }

            var records = await _repository.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var entries = records
                .Where(record => string.Equals(record.TransferType, "delegate", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(record => record.UpdatedUtc)
                .Select(record => new OwnershipRevocationEntryDto
                {
                    RequestId = record.RequestId,
                    Subject = record.ToUserId,
                    Scope = ExtractScope(record.Reason),
                    Reason = record.Reason ?? "no reason provided",
                    RequestedBy = record.FromUserId,
                    Status = MapRevocationStatus(record.Status),
                    UpdatedUtc = record.UpdatedUtc
                })
                .ToList();

            return ServiceResponse<IReadOnlyList<OwnershipRevocationEntryDto>>.Success(entries);
        });
    }

    private static void UpsertNode(
        IDictionary<string, OwnershipLineageNodeDto> nodes,
        string nodeId,
        string role,
        string status)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return;
        }

        if (nodes.TryGetValue(nodeId, out var existing))
        {
            var mergedRole = MergeRole(existing.Role, role);
            var mergedStatus = MergeStatus(existing.Status, status);
            nodes[nodeId] = new OwnershipLineageNodeDto
            {
                NodeId = existing.NodeId,
                Label = existing.Label,
                Role = mergedRole,
                Status = mergedStatus
            };
            return;
        }

        nodes[nodeId] = new OwnershipLineageNodeDto
        {
            NodeId = nodeId,
            Label = nodeId,
            Role = role,
            Status = status
        };
    }

    private static string MergeRole(string existing, string incoming)
    {
        var priority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Owner"] = 3,
            ["Successor"] = 2,
            ["Delegate"] = 1
        };
        var existingScore = priority.TryGetValue(existing, out var exScore) ? exScore : 0;
        var incomingScore = priority.TryGetValue(incoming, out var inScore) ? inScore : 0;
        return incomingScore > existingScore ? incoming : existing;
    }

    private static string MergeStatus(string existing, string incoming)
    {
        var priority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["active"] = 3,
            ["pending"] = 2,
            ["revoked"] = 1
        };
        var existingScore = priority.TryGetValue(existing, out var exScore) ? exScore : 0;
        var incomingScore = priority.TryGetValue(incoming, out var inScore) ? inScore : 0;
        return incomingScore > existingScore ? incoming : existing;
    }

    private static string MapRelationship(string transferType)
    {
        return string.Equals(transferType, "transfer", StringComparison.OrdinalIgnoreCase)
            ? "successor"
            : "delegate";
    }

    private static string MapNodeStatus(string status)
    {
        if (string.Equals(status, "revoked", StringComparison.OrdinalIgnoreCase))
        {
            return "revoked";
        }

        if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return "pending";
        }

        if (string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase))
        {
            return "active";
        }

        return "active";
    }

    private static string MapRevocationStatus(string status)
    {
        if (string.Equals(status, "revoked", StringComparison.OrdinalIgnoreCase))
        {
            return "revoked";
        }

        if (string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase))
        {
            return "confirmed";
        }

        if (string.Equals(status, "pending", StringComparison.OrdinalIgnoreCase))
        {
            return "pending";
        }

        return "revoked";
    }

    private static string ExtractScope(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "General";
        }

        var marker = "scope:";
        var index = reason.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return "General";
        }

        var start = index + marker.Length;
        var remaining = reason[start..].Trim();
        if (string.IsNullOrWhiteSpace(remaining))
        {
            return "General";
        }

        var end = remaining.IndexOf(';');
        var scope = end >= 0 ? remaining[..end] : remaining;
        return scope.Trim().Length == 0 ? "General" : scope.Trim();
    }
}
#nullable disable
