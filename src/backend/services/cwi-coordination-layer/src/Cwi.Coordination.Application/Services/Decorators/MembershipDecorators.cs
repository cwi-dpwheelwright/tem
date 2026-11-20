using Cwi.Common;
using Cwi.Coordination.Application.Models;
namespace Cwi.Coordination.Application.Services.Decorators;

public sealed class MembershipAuditDecorator : IMembershipService
{
    private readonly IMembershipService _inner;
    private readonly LedgerService _ledger;

    public MembershipAuditDecorator(IMembershipService inner, LedgerService ledger)
    {
        _inner = inner;
        _ledger = ledger;
    }

    public async Task<ServiceResponse<EnrollmentResponse>> EnrollAsync(EnrollmentRequest request, CancellationToken cancellationToken)
    {
        var response = await _inner.EnrollAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccess)
        {
            await _ledger.AppendAsync("membership_enroll", request, cancellationToken).ConfigureAwait(false);
        }
        return response;
    }

    public async Task<ServiceResponse<MemberResponse>> RotateAsync(RotationRequest request, CancellationToken cancellationToken)
    {
        var response = await _inner.RotateAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccess)
        {
            await _ledger.AppendAsync("membership_rotate", request, cancellationToken).ConfigureAwait(false);
        }
        return response;
    }

    public async Task<ServiceResponse<MemberResponse>> RevokeAsync(RevokeRequest request, CancellationToken cancellationToken)
    {
        var response = await _inner.RevokeAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccess)
        {
            await _ledger.AppendAsync("membership_revoke", request, cancellationToken).ConfigureAwait(false);
        }
        return response;
    }

    public Task<ServiceResponse<IReadOnlyList<MemberResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        return _inner.ListAsync(cancellationToken);
    }
}

public sealed class MembershipMetricsDecorator : IMembershipService
{
    private readonly IMembershipService _inner;
    private readonly IMetricsRecorder _metrics;

    public MembershipMetricsDecorator(IMembershipService inner, IMetricsRecorder metrics)
    {
        _inner = inner;
        _metrics = metrics;
    }

    public async Task<ServiceResponse<EnrollmentResponse>> EnrollAsync(EnrollmentRequest request, CancellationToken cancellationToken)
    {
        _metrics.Increment("coordination_membership_enroll_total");
        return await _inner.EnrollAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ServiceResponse<MemberResponse>> RotateAsync(RotationRequest request, CancellationToken cancellationToken)
    {
        _metrics.Increment("coordination_membership_rotate_total");
        return await _inner.RotateAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ServiceResponse<MemberResponse>> RevokeAsync(RevokeRequest request, CancellationToken cancellationToken)
    {
        _metrics.Increment("coordination_membership_revoke_total");
        return await _inner.RevokeAsync(request, cancellationToken).ConfigureAwait(false);
    }

    public Task<ServiceResponse<IReadOnlyList<MemberResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        _metrics.Increment("coordination_membership_list_total");
        return _inner.ListAsync(cancellationToken);
    }
}

public sealed class MembershipIdempotencyDecorator : IMembershipService
{
    private readonly IMembershipService _inner;
    private readonly IIdempotencyStore _store;

    public MembershipIdempotencyDecorator(IMembershipService inner, IIdempotencyStore store)
    {
        _inner = inner;
        _store = store;
    }

    public async Task<ServiceResponse<EnrollmentResponse>> EnrollAsync(EnrollmentRequest request, CancellationToken cancellationToken)
    {
        var key = $"enroll:{request.NodeId}";
        if (!_store.TryBegin(key))
        {
            return ServiceResponse<EnrollmentResponse>.Fail(
                new Cwi.Common.Errors.DomainError(Cwi.Common.Errors.ErrorCodes.RateLimited, "Duplicate enrollment."),
                409,
                "Duplicate enrollment");
        }
        try
        {
            return await _inner.EnrollAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _store.Complete(key);
        }
    }

    public Task<ServiceResponse<MemberResponse>> RotateAsync(RotationRequest request, CancellationToken cancellationToken)
        => _inner.RotateAsync(request, cancellationToken);

    public Task<ServiceResponse<MemberResponse>> RevokeAsync(RevokeRequest request, CancellationToken cancellationToken)
        => _inner.RevokeAsync(request, cancellationToken);

    public Task<ServiceResponse<IReadOnlyList<MemberResponse>>> ListAsync(CancellationToken cancellationToken)
        => _inner.ListAsync(cancellationToken);
}
