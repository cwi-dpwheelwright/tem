using Cwi.Common;
using Cwi.Common.Errors;

namespace Cwi.Orchestrator.Application.Survival;

public sealed class SurvivalService : DomainServiceBase
{
    private readonly SurvivalSignalStore _store;
    private readonly SurvivalEvaluator _evaluator;

    public SurvivalService(SurvivalSignalStore store, SurvivalEvaluator evaluator)
    {
        _store = store;
        _evaluator = evaluator;
    }

    public Task<ServiceResponse<SurvivalStatusDto>> RecordAsync(SurvivalSignalDto signal)
    {
        if (string.IsNullOrWhiteSpace(signal.SignalType))
        {
            return Task.FromResult(ServiceResponse<SurvivalStatusDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "signal_type required"),
                statusCode: 400,
                message: "signal_type required"));
        }

        if (signal.Severity < 0 || signal.Severity > 1)
        {
            return Task.FromResult(ServiceResponse<SurvivalStatusDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "severity must be 0-1"),
                statusCode: 400,
                message: "severity must be 0-1"));
        }

        _store.Record(signal);
        return Task.FromResult(ServiceResponse<SurvivalStatusDto>.Success(_evaluator.Evaluate()));
    }

    public Task<ServiceResponse<SurvivalStatusDto>> GetStatusAsync()
    {
        return Task.FromResult(ServiceResponse<SurvivalStatusDto>.Success(_evaluator.Evaluate()));
    }

    public Task<ServiceResponse<OfflineResponseDto>> GetOfflineResponseAsync()
    {
        return Task.FromResult(ServiceResponse<OfflineResponseDto>.Success(BuildOfflineResponse()));
    }

    private OfflineResponseDto BuildOfflineResponse()
    {
        var status = _evaluator.Evaluate();
        if (!string.Equals(status.Mode, "survival", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(status.Mode, "degraded", StringComparison.OrdinalIgnoreCase))
        {
            return new OfflineResponseDto { Mode = "normal", Message = string.Empty };
        }

        return new OfflineResponseDto { Mode = status.Mode, Message = status.OfflineMessage };
    }
}
