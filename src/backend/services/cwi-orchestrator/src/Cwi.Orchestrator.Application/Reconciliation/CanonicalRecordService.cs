using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Reconciliation;

public sealed class CanonicalRecordService : DomainServiceBase
{
    private readonly ICanonicalRecordRepository _repository;
    private readonly ConflictDetector _conflictDetector;
    private readonly RecordReconciler _reconciler;
    private readonly ReconciliationApprovalService _approvalService;

    public CanonicalRecordService(
        ICanonicalRecordRepository repository,
        ConflictDetector conflictDetector,
        RecordReconciler reconciler,
        ReconciliationApprovalService approvalService)
    {
        _repository = repository;
        _conflictDetector = conflictDetector;
        _reconciler = reconciler;
        _approvalService = approvalService;
    }

    public Task<ServiceResponse<CanonicalRecordResponseDto>> UpsertAsync(
        CanonicalRecordUpsertRequestDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RecordId))
        {
            return Task.FromResult(ServiceResponse<CanonicalRecordResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "record_id required"),
                statusCode: 400,
                message: "record_id required"));
        }

        return ExecuteAsync(async () =>
        {
            var sources = request.Sources ?? new List<SourceRecordDto>();
            var canonical = request.Canonical ?? new Dictionary<string, object?>();
            if (canonical.Count == 0)
            {
                canonical = _reconciler.BuildCanonical(sources);
            }
            canonical = RecordReconciler.NormalizeCanonical(canonical);

            var conflicts = _conflictDetector.DetectConflicts(sources);
            var now = DateTimeOffset.UtcNow;

            var entity = new CanonicalRecord
            {
                Id = Guid.NewGuid(),
                RecordId = request.RecordId.Trim(),
                CanonicalJson = JsonSerializer.Serialize(canonical),
                SourcesJson = JsonSerializer.Serialize(sources),
                ConflictsJson = JsonSerializer.Serialize(conflicts),
                UpdatedUtc = now
            };

            var saved = await _repository.UpsertAsync(entity, cancellationToken).ConfigureAwait(false);
            var approvalTicket = await _approvalService
                .EnsureApprovalAsync(
                    request.RecordId,
                    request.TenantId,
                    request.UserId,
                    conflicts,
                    cancellationToken)
                .ConfigureAwait(false);
            return new CanonicalRecordResponseDto
            {
                RecordId = saved.RecordId,
                Canonical = canonical,
                Sources = sources,
                Conflicts = conflicts,
                ApprovalTicket = approvalTicket,
                UpdatedUtc = saved.UpdatedUtc
            };
        });
    }

    public Task<ServiceResponse<CanonicalRecordResponseDto>> GetAsync(string recordId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recordId))
        {
            return Task.FromResult(ServiceResponse<CanonicalRecordResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "record_id required"),
                statusCode: 400,
                message: "record_id required"));
        }

        return ExecuteAsync(async () =>
        {
            var entity = await _repository.GetAsync(recordId, cancellationToken).ConfigureAwait(false);
            if (entity is null)
            {
                return new CanonicalRecordResponseDto
                {
                    RecordId = recordId.Trim(),
                    UpdatedUtc = DateTimeOffset.MinValue
                };
            }

            var approval = await _approvalService
                .GetExistingApprovalAsync(entity.RecordId, string.Empty, cancellationToken)
                .ConfigureAwait(false);

            return new CanonicalRecordResponseDto
            {
                RecordId = entity.RecordId,
                Canonical = JsonSerializer.Deserialize<Dictionary<string, object?>>(entity.CanonicalJson) ?? new(),
                Sources = JsonSerializer.Deserialize<List<SourceRecordDto>>(entity.SourcesJson) ?? new(),
                Conflicts = JsonSerializer.Deserialize<List<ConflictEntryDto>>(entity.ConflictsJson) ?? new(),
                ApprovalTicket = approval,
                UpdatedUtc = entity.UpdatedUtc
            };
        });
    }

    public Task<ServiceResponse<ReconciliationReportDto>> GetReportAsync(string recordId, CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            var record = await _repository.GetAsync(recordId, cancellationToken).ConfigureAwait(false);
            if (record is null)
            {
                return new ReconciliationReportDto { RecordId = recordId };
            }

            var conflicts = JsonSerializer.Deserialize<List<ConflictEntryDto>>(record.ConflictsJson) ?? new();
            var approval = await _approvalService
                .GetExistingApprovalAsync(recordId, string.Empty, cancellationToken)
                .ConfigureAwait(false);

            return new ReconciliationReportDto
            {
                RecordId = record.RecordId,
                Canonical = JsonSerializer.Deserialize<Dictionary<string, object?>>(record.CanonicalJson) ?? new(),
                Conflicts = conflicts,
                ApprovalTicket = approval,
                UpdatedUtc = record.UpdatedUtc
            };
        });
    }
}
