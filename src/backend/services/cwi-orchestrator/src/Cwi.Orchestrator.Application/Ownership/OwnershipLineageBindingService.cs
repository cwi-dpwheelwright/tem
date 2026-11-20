#nullable enable
using System.Text.Json;
using System.Linq;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Ownership;

public sealed class OwnershipLineageBindingService : DomainServiceBase
{
    private readonly IOwnershipLineageRepository _repository;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public OwnershipLineageBindingService(
        IOwnershipLineageRepository repository,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory)
    {
        _repository = repository;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public Task<ServiceResponse<OwnershipLineageBindingResponseDto>> BindAsync(
        OwnershipLineageBindingRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleBindAsync(request, cancellationToken));
    }

    public Task<ServiceResponse<OwnershipLineageResponseDto>> GetAssetLineageAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleLineageAsync(tenantId, cancellationToken));
    }

    private async Task<ServiceResponse<OwnershipLineageBindingResponseDto>> HandleBindAsync(
        OwnershipLineageBindingRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId)
            || string.IsNullOrWhiteSpace(request.AssetId)
            || string.IsNullOrWhiteSpace(request.OwnerId))
        {
            return ServiceResponse<OwnershipLineageBindingResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id, asset_id, owner_id required"),
                statusCode: 400,
                message: "missing required fields");
        }

        var relationship = string.IsNullOrWhiteSpace(request.Relationship)
            ? "owner"
            : request.Relationship.Trim().ToLowerInvariant();

        var record = new OwnershipLineageBinding
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId.Trim(),
            AssetId = request.AssetId.Trim(),
            OwnerId = request.OwnerId.Trim(),
            Relationship = relationship,
            ParentAssetId = string.IsNullOrWhiteSpace(request.ParentAssetId) ? null : request.ParentAssetId.Trim(),
            Reason = request.Reason,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);

        await AppendLedgerEventAsync(record, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<OwnershipLineageBindingResponseDto>.Success(new OwnershipLineageBindingResponseDto
        {
            BindingId = record.Id.ToString("N"),
            Status = "bound",
            CreatedUtc = record.CreatedUtc
        });
    }

    private async Task<ServiceResponse<OwnershipLineageResponseDto>> HandleLineageAsync(
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<OwnershipLineageResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id required"),
                statusCode: 400,
                message: "tenant_id required");
        }

        var records = await _repository.ListByTenantAsync(tenantId.Trim(), cancellationToken).ConfigureAwait(false);
        var nodes = new Dictionary<string, OwnershipLineageNodeDto>(StringComparer.OrdinalIgnoreCase);
        var edges = new List<OwnershipLineageEdgeDto>();

        foreach (var record in records)
        {
            edges.Add(new OwnershipLineageEdgeDto
            {
                From = record.AssetId,
                To = record.OwnerId,
                Scope = "asset",
                Relationship = record.Relationship
            });

            UpsertNode(nodes, record.AssetId, "Asset", "active");
            UpsertNode(nodes, record.OwnerId, "Owner", "active");

            if (!string.IsNullOrWhiteSpace(record.ParentAssetId))
            {
                edges.Add(new OwnershipLineageEdgeDto
                {
                    From = record.ParentAssetId,
                    To = record.AssetId,
                    Scope = "asset",
                    Relationship = "derived"
                });
                UpsertNode(nodes, record.ParentAssetId, "Asset", "active");
            }
        }

        var response = new OwnershipLineageResponseDto
        {
            Nodes = nodes.Values.OrderBy(node => node.Label).ToList(),
            Edges = edges
        };

        return ServiceResponse<OwnershipLineageResponseDto>.Success(response);
    }

    private async Task AppendLedgerEventAsync(
        OwnershipLineageBinding record,
        CancellationToken cancellationToken)
    {
        var traceId = $"ownership_lineage:{record.TenantId}:{record.AssetId}";
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(new
        {
            tenant_id = record.TenantId,
            asset_id = record.AssetId,
            owner_id = record.OwnerId,
            relationship = record.Relationship,
            parent_asset_id = record.ParentAssetId,
            created_utc = record.CreatedUtc
        });

        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId,
            "ownership.lineage_bound",
            payload,
            prevHash ?? string.Empty);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    private static void UpsertNode(
        IDictionary<string, OwnershipLineageNodeDto> nodes,
        string nodeId,
        string role,
        string status)
    {
        if (nodes.ContainsKey(nodeId))
        {
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
}
#nullable disable
