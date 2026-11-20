using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class RoutingHintService
{
    private readonly IMeshTopologyRepository _repository;
    private readonly IRoutingHintEngine _engine;
    private readonly ISurvivalModeRepository _survivalRepository;
    private readonly LedgerService _ledgerService;

    public RoutingHintService(
        IMeshTopologyRepository repository,
        IRoutingHintEngine engine,
        ISurvivalModeRepository survivalRepository,
        LedgerService ledgerService)
    {
        _repository = repository;
        _engine = engine;
        _survivalRepository = survivalRepository;
        _ledgerService = ledgerService;
    }

    public async Task<ServiceResponse<RoutingHintResponse>> GetHintAsync(
        RoutingHintRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            return ServiceResponse<RoutingHintResponse>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "node_id required."),
                statusCode: 400,
                message: "node_id required");
        }

        var states = await _repository.ListByNodeAsync(request.NodeId, cancellationToken).ConfigureAwait(false);
        var survival = await _survivalRepository.GetAsync(cancellationToken).ConfigureAwait(false);

        var complexity = ParseComplexity(request.TaskComplexity);
        var hint = _engine.ComputeHint(request.NodeId, states, complexity, survival.Enabled);
        if (hint is null)
        {
            return ServiceResponse<RoutingHintResponse>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "No routing hint available."),
                statusCode: 503,
                message: "No routing hint available");
        }

        await _ledgerService.AppendAsync("routing.hint", new
        {
            node_id = hint.NodeId,
            preferred_interface = hint.PreferredInterface.ToString().ToLowerInvariant(),
            preferred_path = hint.PreferredPath,
            confidence = hint.Confidence,
            reason = hint.Reason,
            fallback_interfaces = hint.FallbackInterfaces.Select(i => i.ToString().ToLowerInvariant()).ToList(),
            ttl_seconds = hint.TtlSeconds,
            cooldown_seconds = hint.CooldownSeconds,
            hysteresis_margin = hint.HysteresisMargin,
            task_complexity = complexity.ToString().ToLowerInvariant(),
            survival_mode = survival.Enabled,
            observed_utc = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<RoutingHintResponse>.Success(new RoutingHintResponse
        {
            NodeId = hint.NodeId,
            PreferredInterface = hint.PreferredInterface.ToString().ToLowerInvariant(),
            PreferredPath = hint.PreferredPath,
            Confidence = hint.Confidence,
            Reason = hint.Reason,
            FallbackInterfaces = hint.FallbackInterfaces.Select(i => i.ToString().ToLowerInvariant()).ToList(),
            TtlSeconds = hint.TtlSeconds,
            CooldownSeconds = hint.CooldownSeconds,
            HysteresisMargin = hint.HysteresisMargin
        });
    }

    private static TaskComplexity ParseComplexity(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return TaskComplexity.Medium;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "low" => TaskComplexity.Low,
            "high" => TaskComplexity.High,
            _ => TaskComplexity.Medium
        };
    }
}
