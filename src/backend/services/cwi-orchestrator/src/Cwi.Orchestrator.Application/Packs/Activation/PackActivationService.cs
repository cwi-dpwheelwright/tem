#nullable enable
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Packs.Activation;

public sealed class PackActivationService : DomainServiceBase
{
    private readonly IPackActivationRepository _repository;
    private readonly PackActivationAuditRecorder _auditRecorder;

    public PackActivationService(
        IPackActivationRepository repository,
        PackActivationAuditRecorder auditRecorder)
    {
        _repository = repository;
        _auditRecorder = auditRecorder;
    }

    public Task<ServiceResponse<PackActivationResponseDto>> ActivateAsync(
        PackActivationRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(request.TenantId) ||
                string.IsNullOrWhiteSpace(request.PackName) ||
                string.IsNullOrWhiteSpace(request.PackHash) ||
                string.IsNullOrWhiteSpace(request.ActorUserId))
            {
                return ServiceResponse<PackActivationResponseDto>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Activation request is invalid."),
                    statusCode: 400,
                    message: "Activation request is invalid");
            }

            var now = DateTimeOffset.UtcNow;
            var state = await _repository.GetAsync(request.TenantId, request.PackName, cancellationToken)
                .ConfigureAwait(false);

            if (state is null)
            {
                state = new PackActivationState
                {
                    Id = Guid.NewGuid(),
                    TenantId = request.TenantId,
                    PackName = request.PackName,
                    PackHash = request.PackHash,
                    Status = PackActivationStatus.Active,
                    ActivatedBy = request.ActorUserId,
                    ActivatedUtc = now,
                    UpdatedUtc = now
                };

                await _repository.UpsertAsync(state, cancellationToken).ConfigureAwait(false);
                await _auditRecorder.RecordActivationAsync(state, cancellationToken).ConfigureAwait(false);
                return ServiceResponse<PackActivationResponseDto>.Success(MapActivation(state));
            }

            if (string.Equals(state.Status, PackActivationStatus.Active, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(state.PackHash, request.PackHash, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResponse<PackActivationResponseDto>.Success(MapActivation(state));
            }

            state.PackHash = request.PackHash;
            state.Status = PackActivationStatus.Active;
            state.ActivatedBy = request.ActorUserId;
            state.ActivatedUtc = now;
            state.RolledBackUtc = null;
            state.RolledBackBy = string.Empty;
            state.RollbackReason = string.Empty;
            state.UpdatedUtc = now;

            await _repository.UpsertAsync(state, cancellationToken).ConfigureAwait(false);
            await _auditRecorder.RecordActivationAsync(state, cancellationToken).ConfigureAwait(false);

            return ServiceResponse<PackActivationResponseDto>.Success(MapActivation(state));
        });
    }

    public Task<ServiceResponse<PackRollbackResponseDto>> RollbackAsync(
        PackRollbackRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(request.TenantId) ||
                string.IsNullOrWhiteSpace(request.PackName) ||
                string.IsNullOrWhiteSpace(request.ActorUserId) ||
                string.IsNullOrWhiteSpace(request.Reason))
            {
                return ServiceResponse<PackRollbackResponseDto>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "Rollback request is invalid."),
                    statusCode: 400,
                    message: "Rollback request is invalid");
            }

            var now = DateTimeOffset.UtcNow;
            var state = await _repository.GetAsync(request.TenantId, request.PackName, cancellationToken)
                .ConfigureAwait(false);

            if (state is null)
            {
                state = new PackActivationState
                {
                    Id = Guid.NewGuid(),
                    TenantId = request.TenantId,
                    PackName = request.PackName,
                    PackHash = string.Empty,
                    Status = PackActivationStatus.RolledBack,
                    RolledBackBy = request.ActorUserId,
                    RollbackReason = request.Reason,
                    RolledBackUtc = now,
                    ActivatedUtc = now,
                    UpdatedUtc = now
                };

                await _repository.UpsertAsync(state, cancellationToken).ConfigureAwait(false);
                await _auditRecorder.RecordRollbackAsync(state, cancellationToken).ConfigureAwait(false);
                return ServiceResponse<PackRollbackResponseDto>.Success(MapRollback(state));
            }

            if (string.Equals(state.Status, PackActivationStatus.RolledBack, StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResponse<PackRollbackResponseDto>.Success(MapRollback(state));
            }

            state.Status = PackActivationStatus.RolledBack;
            state.RolledBackBy = request.ActorUserId;
            state.RollbackReason = request.Reason;
            state.RolledBackUtc = now;
            state.UpdatedUtc = now;

            await _repository.UpsertAsync(state, cancellationToken).ConfigureAwait(false);
            await _auditRecorder.RecordRollbackAsync(state, cancellationToken).ConfigureAwait(false);

            return ServiceResponse<PackRollbackResponseDto>.Success(MapRollback(state));
        });
    }

    private static PackActivationResponseDto MapActivation(PackActivationState state)
    {
        return new PackActivationResponseDto
        {
            TenantId = state.TenantId,
            PackName = state.PackName,
            PackHash = state.PackHash,
            Status = state.Status,
            ActivatedUtc = state.ActivatedUtc,
            RolledBackUtc = state.RolledBackUtc,
            UpdatedUtc = state.UpdatedUtc
        };
    }

    private static PackRollbackResponseDto MapRollback(PackActivationState state)
    {
        return new PackRollbackResponseDto
        {
            TenantId = state.TenantId,
            PackName = state.PackName,
            PackHash = state.PackHash,
            Status = state.Status,
            RolledBackUtc = state.RolledBackUtc,
            UpdatedUtc = state.UpdatedUtc
        };
    }
}
#nullable disable
