using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application;
using Cwi.Coordination.Application.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Transport;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class NodeWorkService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly INodeWorkRepository _repository;
    private readonly IMembershipRepository _membershipRepository;
    private readonly LedgerService _ledger;
    private readonly INodeSessionDispatcher _dispatcher;
    private readonly NodeSessionOptions _sessionOptions;

    public NodeWorkService(
        INodeWorkRepository repository,
        IMembershipRepository membershipRepository,
        LedgerService ledger)
        : this(
            repository,
            membershipRepository,
            ledger,
            NoopNodeSessionDispatcher.Instance,
            new NodeSessionOptions())
    {
    }

    public NodeWorkService(
        INodeWorkRepository repository,
        IMembershipRepository membershipRepository,
        LedgerService ledger,
        INodeSessionDispatcher dispatcher,
        NodeSessionOptions sessionOptions)
    {
        _repository = repository;
        _membershipRepository = membershipRepository;
        _ledger = ledger;
        _dispatcher = dispatcher;
        _sessionOptions = sessionOptions;
    }

    public async Task<ServiceResponse<NodeWorkItemResponse>> CreateAsync(
        CreateNodeWorkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            ValidateCreateRequest(request);
            await EnsureActiveMemberAsync(request.NodeId, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var existingByKey = await _repository.GetByIdempotencyKeyAsync(request.IdempotencyKey, cancellationToken)
                    .ConfigureAwait(false);
                if (existingByKey is not null)
                {
                    return ServiceResponse<NodeWorkItemResponse>.Success(ToResponse(existingByKey));
                }
            }

            var workId = string.IsNullOrWhiteSpace(request.WorkId)
                ? Guid.NewGuid().ToString("n")
                : request.WorkId;

            var existingById = await _repository.GetAsync(workId, cancellationToken).ConfigureAwait(false);
            if (existingById is not null)
            {
                return ServiceResponse<NodeWorkItemResponse>.Success(ToResponse(existingById));
            }

            var nowUtc = DateTimeOffset.UtcNow;
            var availableUtc = request.AvailableUtc ?? nowUtc;
            if (request.ExpiresUtc is not null && request.ExpiresUtc <= availableUtc)
            {
                throw new DomainException(
                    new DomainError(ErrorCodes.InvalidRequest, "expires_utc must be later than available_utc."),
                    400);
            }

            var item = new NodeWorkItem
            {
                WorkId = workId,
                RequestId = NormalizeOptional(request.RequestId),
                TraceId = NormalizeOptional(request.TraceId),
                NodeId = request.NodeId,
                TenantId = request.TenantId,
                UserId = NormalizeOptional(request.UserId),
                TaskType = request.TaskType.Trim(),
                PolicyScope = NormalizeOptional(request.PolicyScope),
                PayloadJson = SerializeDictionary(request.Payload),
                WorkToken = NormalizeOptional(request.WorkToken),
                IdempotencyKey = NormalizeOptional(request.IdempotencyKey),
                Status = NodeWorkStatus.Queued,
                CreatedUtc = nowUtc,
                AvailableUtc = availableUtc,
                ExpiresUtc = request.ExpiresUtc,
                UpdatedUtc = nowUtc
            };

            await _repository.AddAsync(item, cancellationToken).ConfigureAwait(false);
            await _ledger.AppendAsync(
                "node_work.created",
                new
                {
                    item.WorkId,
                    item.RequestId,
                    item.TraceId,
                    item.NodeId,
                    item.TenantId,
                    item.UserId,
                    item.TaskType,
                    item.PolicyScope
                },
                cancellationToken).ConfigureAwait(false);

            await DispatchPendingAsync(item.NodeId, cancellationToken).ConfigureAwait(false);
            var persisted = await _repository.GetAsync(item.WorkId, cancellationToken).ConfigureAwait(false) ?? item;
            return ServiceResponse<NodeWorkItemResponse>.Success(ToResponse(persisted));
        }
        catch (DomainException exc)
        {
            return ServiceResponse<NodeWorkItemResponse>.Fail(exc.Error, exc.StatusCode, exc.Message);
        }
    }

    public async Task<int> DispatchPendingAsync(string nodeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || !_dispatcher.HasActiveSession(nodeId))
        {
            return 0;
        }

        try
        {
            await EnsureActiveMemberAsync(nodeId, cancellationToken).ConfigureAwait(false);
        }
        catch (DomainException)
        {
            return 0;
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var items = await _repository.ListLeasableAsync(
                nodeId,
                nowUtc,
                _sessionOptions.MaxPendingDispatchPerSession,
                cancellationToken)
            .ConfigureAwait(false);

        var dispatched = 0;
        foreach (var item in items)
        {
            var originalStatus = item.Status;
            var originalLeaseId = item.LeaseId;
            var originalLeaseExpiresUtc = item.LeaseExpiresUtc;
            var originalLeaseCount = item.LeaseCount;
            var originalCompletedUtc = item.CompletedUtc;

            item.Status = NodeWorkStatus.Leased;
            item.LeaseId = Guid.NewGuid().ToString("n");
            item.LeaseExpiresUtc = nowUtc.AddSeconds(_sessionOptions.DispatchAckTimeoutSeconds);
            item.LeaseCount += 1;
            item.CompletedUtc = null;
            item.UpdatedUtc = nowUtc;
            await _repository.UpdateAsync(item, cancellationToken).ConfigureAwait(false);

            var envelope = ToEnvelope(item);
            var sent = await _dispatcher.TryDispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            if (!sent)
            {
                item.Status = originalStatus;
                item.LeaseId = originalLeaseId;
                item.LeaseExpiresUtc = originalLeaseExpiresUtc;
                item.LeaseCount = originalLeaseCount;
                item.CompletedUtc = originalCompletedUtc;
                item.UpdatedUtc = DateTimeOffset.UtcNow;
                await _repository.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
                break;
            }

            await _ledger.AppendAsync(
                "node_work.dispatched",
                new
                {
                    item.WorkId,
                    item.RequestId,
                    item.NodeId,
                    item.LeaseId,
                    item.PolicyScope,
                    envelope.RequestKind
                },
                cancellationToken).ConfigureAwait(false);
            dispatched += 1;
        }

        return dispatched;
    }

    public async Task<ServiceResponse<IReadOnlyList<NodeWorkItemResponse>>> ListAsync(
        string? tenantId,
        string? nodeId,
        string? status,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(status) && !Enum.TryParse<NodeWorkStatus>(status, true, out _))
            {
                throw new DomainException(
                    new DomainError(ErrorCodes.InvalidRequest, "status must be queued, leased, acknowledged, completed, failed, denied, or expired."),
                    400);
            }

            var items = await _repository.ListAsync(tenantId, nodeId, status, cancellationToken).ConfigureAwait(false);
            return ServiceResponse<IReadOnlyList<NodeWorkItemResponse>>.Success(items.Select(ToResponse).ToList());
        }
        catch (DomainException exc)
        {
            return ServiceResponse<IReadOnlyList<NodeWorkItemResponse>>.Fail(exc.Error, exc.StatusCode, exc.Message);
        }
    }

    public async Task<ServiceResponse<NodeWorkItemResponse>> GetAsync(
        string workId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(workId))
            {
                throw new DomainException(
                    new DomainError(ErrorCodes.InvalidRequest, "work_id is required."),
                    400);
            }

            var item = await _repository.GetAsync(workId.Trim(), cancellationToken).ConfigureAwait(false);
            if (item is null)
            {
                throw new DomainException(
                    new DomainError(ErrorCodes.InvalidRequest, "Work item not found."),
                    404);
            }

            return ServiceResponse<NodeWorkItemResponse>.Success(ToResponse(item));
        }
        catch (DomainException exc)
        {
            return ServiceResponse<NodeWorkItemResponse>.Fail(exc.Error, exc.StatusCode, exc.Message);
        }
    }

    public async Task<ServiceResponse<LeaseNodeWorkResponse>> LeaseAsync(
        LeaseNodeWorkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.NodeId))
            {
                throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "node_id is required."), 400);
            }

            if (request.MaxItems <= 0 || request.MaxItems > 100)
            {
                throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "max_items must be between 1 and 100."), 400);
            }

            if (request.LeaseDurationSeconds <= 0 || request.LeaseDurationSeconds > 3600)
            {
                throw new DomainException(
                    new DomainError(ErrorCodes.InvalidRequest, "lease_duration_seconds must be between 1 and 3600."),
                    400);
            }

            await EnsureActiveMemberAsync(request.NodeId, cancellationToken).ConfigureAwait(false);

            var nowUtc = DateTimeOffset.UtcNow;
            var leaseExpiresUtc = nowUtc.AddSeconds(request.LeaseDurationSeconds);
            var items = await _repository.ListLeasableAsync(request.NodeId, nowUtc, request.MaxItems, cancellationToken)
                .ConfigureAwait(false);

            var leased = new List<NodeWorkItemResponse>(items.Count);
            foreach (var item in items)
            {
                item.Status = NodeWorkStatus.Leased;
                item.LeaseId = Guid.NewGuid().ToString("n");
                item.LeaseExpiresUtc = leaseExpiresUtc;
                item.LeaseCount += 1;
                item.UpdatedUtc = nowUtc;
                item.CompletedUtc = null;
                await _repository.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
                await _ledger.AppendAsync(
                    "node_work.leased",
                    new { item.WorkId, item.RequestId, item.NodeId, item.LeaseId, item.LeaseCount },
                    cancellationToken).ConfigureAwait(false);
                leased.Add(ToResponse(item));
            }

            return ServiceResponse<LeaseNodeWorkResponse>.Success(new LeaseNodeWorkResponse
            {
                Items = leased
            });
        }
        catch (DomainException exc)
        {
            return ServiceResponse<LeaseNodeWorkResponse>.Fail(exc.Error, exc.StatusCode, exc.Message);
        }
    }

    public async Task<ServiceResponse<NodeWorkItemResponse>> AcknowledgeAsync(
        string workId,
        AckNodeWorkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var item = await ValidateLeaseMutationAsync(workId, request.NodeId, request.LeaseId, cancellationToken)
                .ConfigureAwait(false);

            var nowUtc = DateTimeOffset.UtcNow;
            item.Status = NodeWorkStatus.Acknowledged;
            item.LeaseExpiresUtc = nowUtc.AddSeconds(_sessionOptions.DispatchExecutionTimeoutSeconds);
            item.UpdatedUtc = nowUtc;
            await _repository.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
            await _ledger.AppendAsync(
                "node_work.acknowledged",
                new { item.WorkId, item.RequestId, item.NodeId, item.LeaseId, item.LeaseExpiresUtc },
                cancellationToken).ConfigureAwait(false);

            return ServiceResponse<NodeWorkItemResponse>.Success(ToResponse(item));
        }
        catch (DomainException exc)
        {
            return ServiceResponse<NodeWorkItemResponse>.Fail(exc.Error, exc.StatusCode, exc.Message);
        }
    }

    public async Task<ServiceResponse<NodeWorkItemResponse>> CompleteAsync(
        string workId,
        CompleteNodeWorkRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _repository.GetAsync(workId, cancellationToken).ConfigureAwait(false);
            if (existing is not null && IsTerminalStatus(existing.Status))
            {
                if (!string.Equals(existing.NodeId, request.NodeId, StringComparison.Ordinal))
                {
                    throw new DomainException(new DomainError(ErrorCodes.Forbidden, "Work item does not belong to node."), 403);
                }

                if (!string.Equals(existing.LeaseId, request.LeaseId, StringComparison.Ordinal))
                {
                    throw new DomainException(new DomainError(ErrorCodes.Forbidden, "Lease does not match work item."), 403);
                }

                return ServiceResponse<NodeWorkItemResponse>.Success(ToResponse(existing));
            }

            var item = await ValidateLeaseMutationAsync(workId, request.NodeId, request.LeaseId, cancellationToken)
                .ConfigureAwait(false);

            if (!TryParseCompletionStatus(request.Status, out var completionStatus))
            {
                throw new DomainException(
                    new DomainError(ErrorCodes.InvalidRequest, "status must be completed, failed, or denied."),
                    400);
            }

            var nowUtc = DateTimeOffset.UtcNow;
            item.Status = completionStatus;
            item.ResultJson = SerializeDictionary(request.Result);
            item.CompletedUtc = nowUtc;
            item.LeaseExpiresUtc = null;
            item.UpdatedUtc = nowUtc;
            await _repository.UpdateAsync(item, cancellationToken).ConfigureAwait(false);
            await _ledger.AppendAsync(
                $"node_work.{completionStatus.ToString().ToLowerInvariant()}",
                new { item.WorkId, item.RequestId, item.NodeId, item.LeaseId, Status = completionStatus.ToString().ToLowerInvariant() },
                cancellationToken).ConfigureAwait(false);

            return ServiceResponse<NodeWorkItemResponse>.Success(ToResponse(item));
        }
        catch (DomainException exc)
        {
            return ServiceResponse<NodeWorkItemResponse>.Fail(exc.Error, exc.StatusCode, exc.Message);
        }
    }

    private async Task EnsureActiveMemberAsync(string nodeId, CancellationToken cancellationToken)
    {
        var member = await _membershipRepository.GetAsync(nodeId, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "Node not found."), 404);
        }

        if (member.Status != MembershipStatus.Active)
        {
            throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "Node is not active."), 409);
        }

        member.LastSeenUtc = DateTimeOffset.UtcNow;
        await _membershipRepository.UpdateAsync(member, cancellationToken).ConfigureAwait(false);
    }

    private async Task<NodeWorkItem> ValidateLeaseMutationAsync(
        string workId,
        string nodeId,
        string leaseId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workId))
        {
            throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "work_id is required."), 400);
        }

        if (string.IsNullOrWhiteSpace(nodeId) || string.IsNullOrWhiteSpace(leaseId))
        {
            throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "node_id and lease_id are required."), 400);
        }

        await EnsureActiveMemberAsync(nodeId, cancellationToken).ConfigureAwait(false);

        var item = await _repository.GetAsync(workId, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "Work item not found."), 404);
        }

        if (!string.Equals(item.NodeId, nodeId, StringComparison.Ordinal))
        {
            throw new DomainException(new DomainError(ErrorCodes.Forbidden, "Work item does not belong to node."), 403);
        }

        if (!string.Equals(item.LeaseId, leaseId, StringComparison.Ordinal))
        {
            throw new DomainException(new DomainError(ErrorCodes.Forbidden, "Lease does not match work item."), 403);
        }

        if (item.LeaseExpiresUtc is not null && item.LeaseExpiresUtc <= DateTimeOffset.UtcNow)
        {
            throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "Lease expired."), 409);
        }

        if (item.Status is not NodeWorkStatus.Leased and not NodeWorkStatus.Acknowledged)
        {
            throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "Work item is not leased."), 409);
        }

        return item;
    }

    private static void ValidateCreateRequest(CreateNodeWorkRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "node_id is required."), 400);
        }

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."), 400);
        }

        if (string.IsNullOrWhiteSpace(request.TaskType))
        {
            throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "task_type is required."), 400);
        }
    }

    private static bool TryParseCompletionStatus(string value, out NodeWorkStatus status)
    {
        if (Enum.TryParse<NodeWorkStatus>(value, true, out status)
            && status is NodeWorkStatus.Completed or NodeWorkStatus.Failed or NodeWorkStatus.Denied)
        {
            return true;
        }

        status = default;
        return false;
    }

    private static bool IsTerminalStatus(NodeWorkStatus status)
    {
        return status is NodeWorkStatus.Completed or NodeWorkStatus.Failed or NodeWorkStatus.Denied;
    }

    private static NodeWorkEnvelopeMessage ToEnvelope(NodeWorkItem item)
    {
        return new NodeWorkEnvelopeMessage
        {
            WorkId = item.WorkId,
            RequestId = item.RequestId,
            LeaseId = item.LeaseId ?? string.Empty,
            RequestKind = ResolveRequestKind(item.TaskType),
            TaskType = item.TaskType,
            TargetNodeId = item.NodeId,
            PolicyScope = item.PolicyScope,
            TraceContext = new NodeWorkTraceContext
            {
                TraceId = item.TraceId,
                RequestOrigin = "coordination-layer"
            },
            Token = item.WorkToken,
            Payload = DeserializeDictionary(item.PayloadJson)
        };
    }

    private static NodeWorkItemResponse ToResponse(NodeWorkItem item)
    {
        return new NodeWorkItemResponse
        {
            WorkId = item.WorkId,
            RequestId = item.RequestId,
            TraceId = item.TraceId,
            NodeId = item.NodeId,
            TenantId = item.TenantId,
            UserId = item.UserId,
            TaskType = item.TaskType,
            PolicyScope = item.PolicyScope,
            Payload = DeserializeDictionary(item.PayloadJson),
            WorkToken = item.WorkToken,
            IdempotencyKey = item.IdempotencyKey,
            Status = item.Status.ToString().ToLowerInvariant(),
            CreatedUtc = item.CreatedUtc,
            AvailableUtc = item.AvailableUtc,
            ExpiresUtc = item.ExpiresUtc,
            LeaseId = item.LeaseId,
            LeaseExpiresUtc = item.LeaseExpiresUtc,
            LeaseCount = item.LeaseCount,
            Result = DeserializeDictionary(item.ResultJson),
            CompletedUtc = item.CompletedUtc
        };
    }

    private static string SerializeDictionary(Dictionary<string, object?> value)
    {
        return JsonSerializer.Serialize(value ?? new Dictionary<string, object?>(), JsonOptions);
    }

    private static Dictionary<string, object?> DeserializeDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, object?>();
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions)
            ?? new Dictionary<string, object?>();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string ResolveRequestKind(string taskType)
    {
        if (string.IsNullOrWhiteSpace(taskType))
        {
            return "unknown";
        }

        var separator = taskType.IndexOf('.');
        return separator > 0 ? taskType[..separator] : taskType;
    }
}
