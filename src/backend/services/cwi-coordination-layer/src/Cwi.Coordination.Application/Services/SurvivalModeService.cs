using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class SurvivalModeService
{
    private readonly ISurvivalModeRepository _repository;
    private readonly LedgerService _ledgerService;

    public SurvivalModeService(ISurvivalModeRepository repository, LedgerService ledgerService)
    {
        _repository = repository;
        _ledgerService = ledgerService;
    }

    public async Task<ServiceResponse<SurvivalModeResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var state = await _repository.GetAsync(cancellationToken).ConfigureAwait(false);
        return ServiceResponse<SurvivalModeResponse>.Success(Map(state));
    }

    public async Task<ServiceResponse<SurvivalModeResponse>> SetAsync(
        SurvivalModeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Enabled && string.IsNullOrWhiteSpace(request.Reason))
        {
            return ServiceResponse<SurvivalModeResponse>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "reason required when enabling survival mode."),
                statusCode: 400,
                message: "reason required");
        }

        var state = new SurvivalModeState
        {
            Enabled = request.Enabled,
            Reason = request.Reason,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        var saved = await _repository.SetAsync(state, cancellationToken).ConfigureAwait(false);
        await _ledgerService.AppendAsync("survival.mode.set", new
        {
            enabled = saved.Enabled,
            reason = saved.Reason,
            updated_utc = saved.UpdatedUtc
        }, cancellationToken).ConfigureAwait(false);
        return ServiceResponse<SurvivalModeResponse>.Success(Map(saved));
    }

    private static SurvivalModeResponse Map(SurvivalModeState state)
    {
        return new SurvivalModeResponse
        {
            Enabled = state.Enabled,
            Reason = state.Reason,
            UpdatedUtc = state.UpdatedUtc
        };
    }
}
