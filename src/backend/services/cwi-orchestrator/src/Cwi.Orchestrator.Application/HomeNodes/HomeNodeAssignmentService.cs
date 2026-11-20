#nullable enable
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.HomeNodes;

public sealed class HomeNodeAssignmentService : DomainServiceBase
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHomeNodeAssignmentRepository _repository;
    private readonly IHomeNodeRepository _homeNodeRepository;
    private readonly IHomeNodePolicyScopeRepository _policyScopeRepository;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;

    public HomeNodeAssignmentService(
        IHomeNodeAssignmentRepository repository,
        IHomeNodeRepository homeNodeRepository,
        IHomeNodePolicyScopeRepository policyScopeRepository,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory)
    {
        _repository = repository;
        _homeNodeRepository = homeNodeRepository;
        _policyScopeRepository = policyScopeRepository;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
    }

    public Task<ServiceResponse<HomeNodeAssignmentDto>> UpsertAsync(
        HomeNodeAssignmentUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleUpsertAsync(request, cancellationToken));
    }

    public Task<ServiceResponse<IReadOnlyList<HomeNodeAssignmentDto>>> ListAsync(
        string tenantId,
        string? userId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleListAsync(tenantId, userId, cancellationToken));
    }

    public Task<ServiceResponse<HomeNodeAssignmentDto>> GetAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleGetAsync(tenantId, userId, cancellationToken));
    }

    private async Task<ServiceResponse<HomeNodeAssignmentDto>> HandleUpsertAsync(
        HomeNodeAssignmentUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return Fail<HomeNodeAssignmentDto>("tenant_id is required.", "tenant_id required");
        }

        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return Fail<HomeNodeAssignmentDto>("user_id is required.", "user_id required");
        }

        if (string.IsNullOrWhiteSpace(request.PrimaryHomeNodeId))
        {
            return Fail<HomeNodeAssignmentDto>("primary_home_node_id is required.", "primary_home_node_id required");
        }

        var tenantId = request.TenantId.Trim();
        var userId = request.UserId.Trim();
        var primaryHomeNodeId = request.PrimaryHomeNodeId.Trim();
        var watcherNodeId = NormalizeNullable(request.WatcherNodeId);
        var policyScopeId = NormalizeNullable(request.PolicyScopeId);

        var primaryNode = await _homeNodeRepository.GetAsync(tenantId, primaryHomeNodeId, cancellationToken).ConfigureAwait(false);
        if (primaryNode is null)
        {
            return ServiceResponse<HomeNodeAssignmentDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "primary_home_node_id does not reference a registered node."),
                statusCode: 404,
                message: "primary_home_node_id not found");
        }

        if (!string.IsNullOrWhiteSpace(watcherNodeId))
        {
            var watcherNode = await _homeNodeRepository.GetAsync(tenantId, watcherNodeId, cancellationToken).ConfigureAwait(false);
            if (watcherNode is null)
            {
                return ServiceResponse<HomeNodeAssignmentDto>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "watcher_node_id does not reference a registered node."),
                    statusCode: 404,
                    message: "watcher_node_id not found");
            }
        }

        if (!string.IsNullOrWhiteSpace(policyScopeId))
        {
            var scope = await _policyScopeRepository.GetAsync(tenantId, policyScopeId, cancellationToken).ConfigureAwait(false);
            if (scope is null)
            {
                return ServiceResponse<HomeNodeAssignmentDto>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "policy_scope_id does not reference a registered policy scope."),
                    statusCode: 404,
                    message: "policy_scope_id not found");
            }
        }

        var now = DateTimeOffset.UtcNow;
        var existing = await _repository.GetAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);

        var assignment = existing ?? new HomeNodeAssignment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            AssignedUtc = request.AssignedUtc ?? now
        };

        assignment.PrimaryHomeNodeId = primaryHomeNodeId;
        assignment.WatcherNodeId = watcherNodeId;
        assignment.PolicyScopeId = policyScopeId;
        assignment.LocalOnly = request.LocalOnly;
        assignment.RemoteFallbackAllowed = request.RemoteFallbackAllowed;
        assignment.Status = string.IsNullOrWhiteSpace(request.Status)
            ? existing?.Status ?? "active"
            : request.Status.Trim().ToLowerInvariant();
        assignment.AssignedUtc = request.AssignedUtc ?? existing?.AssignedUtc ?? now;
        assignment.RevokedUtc = string.Equals(assignment.Status, "revoked", StringComparison.OrdinalIgnoreCase) ? now : null;
        assignment.UpdatedUtc = now;

        await _repository.UpsertAsync(assignment, cancellationToken).ConfigureAwait(false);
        await AppendLedgerEventAsync(assignment, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<HomeNodeAssignmentDto>.Success(Map(assignment));
    }

    private async Task<ServiceResponse<IReadOnlyList<HomeNodeAssignmentDto>>> HandleListAsync(
        string tenantId,
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Fail<IReadOnlyList<HomeNodeAssignmentDto>>("tenant_id is required.", "tenant_id required");
        }

        var records = await _repository.ListAsync(tenantId.Trim(), NormalizeNullable(userId), cancellationToken).ConfigureAwait(false);
        return ServiceResponse<IReadOnlyList<HomeNodeAssignmentDto>>.Success(records.Select(Map).ToList());
    }

    private async Task<ServiceResponse<HomeNodeAssignmentDto>> HandleGetAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Fail<HomeNodeAssignmentDto>("tenant_id is required.", "tenant_id required");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Fail<HomeNodeAssignmentDto>("user_id is required.", "user_id required");
        }

        var assignment = await _repository.GetAsync(tenantId.Trim(), userId.Trim(), cancellationToken).ConfigureAwait(false);
        if (assignment is null)
        {
            return ServiceResponse<HomeNodeAssignmentDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "home node assignment not found."),
                statusCode: 404,
                message: "home node assignment not found");
        }

        return ServiceResponse<HomeNodeAssignmentDto>.Success(Map(assignment));
    }

    private async Task AppendLedgerEventAsync(HomeNodeAssignment assignment, CancellationToken cancellationToken)
    {
        var traceId = $"home_node_assignment:{assignment.TenantId}:{assignment.UserId}";
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken).ConfigureAwait(false);
        var payload = JsonSerializer.Serialize(new
        {
            tenant_id = assignment.TenantId,
            user_id = assignment.UserId,
            primary_home_node_id = assignment.PrimaryHomeNodeId,
            watcher_node_id = assignment.WatcherNodeId,
            policy_scope_id = assignment.PolicyScopeId,
            local_only = assignment.LocalOnly,
            remote_fallback_allowed = assignment.RemoteFallbackAllowed,
            status = assignment.Status,
            assigned_utc = assignment.AssignedUtc,
            revoked_utc = assignment.RevokedUtc,
            updated_utc = assignment.UpdatedUtc
        }, SerializerOptions);

        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId,
            "home_node.assignment_upserted",
            payload,
            prevHash ?? string.Empty);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    private static HomeNodeAssignmentDto Map(HomeNodeAssignment assignment)
    {
        return new HomeNodeAssignmentDto
        {
            TenantId = assignment.TenantId,
            UserId = assignment.UserId,
            PrimaryHomeNodeId = assignment.PrimaryHomeNodeId,
            WatcherNodeId = assignment.WatcherNodeId,
            PolicyScopeId = assignment.PolicyScopeId,
            LocalOnly = assignment.LocalOnly,
            RemoteFallbackAllowed = assignment.RemoteFallbackAllowed,
            Status = assignment.Status,
            AssignedUtc = assignment.AssignedUtc,
            RevokedUtc = assignment.RevokedUtc,
            UpdatedUtc = assignment.UpdatedUtc
        };
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
