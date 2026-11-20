#nullable enable
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.HomeNodes;

public sealed class HomeNodeRegistryService : DomainServiceBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHomeNodeRepository _repository;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public HomeNodeRegistryService(
        IHomeNodeRepository repository,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory)
    {
        _repository = repository;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public Task<ServiceResponse<HomeNodeDto>> UpsertAsync(
        HomeNodeUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleUpsertAsync(request, cancellationToken));
    }

    public Task<ServiceResponse<IReadOnlyList<HomeNodeDto>>> ListAsync(
        string tenantId,
        string? ownerUserId,
        string? status,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleListAsync(tenantId, ownerUserId, status, cancellationToken));
    }

    public Task<ServiceResponse<HomeNodeDto>> GetAsync(
        string tenantId,
        string nodeId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleGetAsync(tenantId, nodeId, cancellationToken));
    }

    private async Task<ServiceResponse<HomeNodeDto>> HandleUpsertAsync(
        HomeNodeUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return Fail<HomeNodeDto>("tenant_id is required.", "tenant_id required");
        }

        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            return Fail<HomeNodeDto>("node_id is required.", "node_id required");
        }

        if (string.IsNullOrWhiteSpace(request.OwnerUserId))
        {
            return Fail<HomeNodeDto>("owner_user_id is required.", "owner_user_id required");
        }

        var tenantId = request.TenantId.Trim();
        var nodeId = request.NodeId.Trim();
        var ownerUserId = request.OwnerUserId.Trim();
        var now = DateTimeOffset.UtcNow;
        var existing = await _repository.GetAsync(tenantId, nodeId, cancellationToken).ConfigureAwait(false);

        var record = existing ?? new HomeNodeRecord
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            NodeId = nodeId,
            CreatedUtc = now
        };

        record.OwnerUserId = ownerUserId;
        record.DeviceClass = NormalizeNullable(request.DeviceClass);
        record.TailscaleIp = NormalizeNullable(request.TailscaleIp);
        record.WatcherNodeId = NormalizeNullable(request.WatcherNodeId);
        record.CapabilitiesJson = SerializeMap(request.Capabilities);
        record.PackVersionsJson = SerializeMap(request.PackVersions);
        record.PolicyProfileVersion = string.IsNullOrWhiteSpace(request.PolicyProfileVersion)
            ? existing?.PolicyProfileVersion ?? "1.0"
            : request.PolicyProfileVersion.Trim();
        record.Status = string.IsNullOrWhiteSpace(request.Status)
            ? existing?.Status ?? "registered"
            : request.Status.Trim().ToLowerInvariant();
        record.LastHeartbeatUtc = request.LastHeartbeatUtc ?? existing?.LastHeartbeatUtc;
        record.AssignedUtc = request.AssignedUtc ?? existing?.AssignedUtc;
        record.UpdatedUtc = now;

        await _repository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        await AppendLedgerEventAsync(record, existing is null ? "home_node.registered" : "home_node.updated", cancellationToken)
            .ConfigureAwait(false);

        return ServiceResponse<HomeNodeDto>.Success(Map(record));
    }

    private async Task<ServiceResponse<IReadOnlyList<HomeNodeDto>>> HandleListAsync(
        string tenantId,
        string? ownerUserId,
        string? status,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Fail<IReadOnlyList<HomeNodeDto>>("tenant_id is required.", "tenant_id required");
        }

        var records = await _repository.ListAsync(
            tenantId.Trim(),
            NormalizeNullable(ownerUserId),
            NormalizeNullable(status),
            cancellationToken).ConfigureAwait(false);

        return ServiceResponse<IReadOnlyList<HomeNodeDto>>.Success(records.Select(Map).ToList());
    }

    private async Task<ServiceResponse<HomeNodeDto>> HandleGetAsync(
        string tenantId,
        string nodeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Fail<HomeNodeDto>("tenant_id is required.", "tenant_id required");
        }

        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return Fail<HomeNodeDto>("node_id is required.", "node_id required");
        }

        var record = await _repository.GetAsync(tenantId.Trim(), nodeId.Trim(), cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            return ServiceResponse<HomeNodeDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "home node not found."),
                statusCode: 404,
                message: "home node not found");
        }

        return ServiceResponse<HomeNodeDto>.Success(Map(record));
    }

    private async Task AppendLedgerEventAsync(HomeNodeRecord record, string eventType, CancellationToken cancellationToken)
    {
        var traceId = $"home_node:{record.TenantId}:{record.NodeId}";
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(new
        {
            tenant_id = record.TenantId,
            node_id = record.NodeId,
            owner_user_id = record.OwnerUserId,
            device_class = record.DeviceClass,
            tailscale_ip = record.TailscaleIp,
            watcher_node_id = record.WatcherNodeId,
            status = record.Status,
            policy_profile_version = record.PolicyProfileVersion,
            last_heartbeat_utc = record.LastHeartbeatUtc,
            assigned_utc = record.AssignedUtc,
            updated_utc = record.UpdatedUtc
        }, SerializerOptions);

        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId,
            eventType,
            payload,
            prevHash ?? string.Empty);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    private static HomeNodeDto Map(HomeNodeRecord record)
    {
        return new HomeNodeDto
        {
            TenantId = record.TenantId,
            NodeId = record.NodeId,
            OwnerUserId = record.OwnerUserId,
            DeviceClass = record.DeviceClass,
            TailscaleIp = record.TailscaleIp,
            WatcherNodeId = record.WatcherNodeId,
            Capabilities = DeserializeMap(record.CapabilitiesJson),
            PackVersions = DeserializeMap(record.PackVersionsJson),
            PolicyProfileVersion = record.PolicyProfileVersion,
            Status = record.Status,
            LastHeartbeatUtc = record.LastHeartbeatUtc,
            AssignedUtc = record.AssignedUtc,
            CreatedUtc = record.CreatedUtc,
            UpdatedUtc = record.UpdatedUtc
        };
    }

    private static string SerializeMap(IReadOnlyDictionary<string, object?>? value)
    {
        return JsonSerializer.Serialize(value ?? new Dictionary<string, object?>(), SerializerOptions);
    }

    private static IReadOnlyDictionary<string, object?> DeserializeMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, object?>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, SerializerOptions)
            ?? new Dictionary<string, object?>();
    }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ServiceResponse<T> Fail<T>(string errorMessage, string message)
    {
        return ServiceResponse<T>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, errorMessage),
            statusCode: 400,
            message: message);
    }
}
#nullable disable
