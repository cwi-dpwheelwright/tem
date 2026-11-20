using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class RerouteGuidanceService
{
    private readonly IMeshTopologyRepository _repository;
    private readonly IMeshEventRepository _eventRepository;
    private readonly ISurvivalModeRepository _survivalRepository;
    private readonly IRoutingHintEngine _engine;
    private readonly LedgerService _ledgerService;

    public RerouteGuidanceService(
        IMeshTopologyRepository repository,
        IMeshEventRepository eventRepository,
        ISurvivalModeRepository survivalRepository,
        IRoutingHintEngine engine,
        LedgerService ledgerService)
    {
        _repository = repository;
        _eventRepository = eventRepository;
        _survivalRepository = survivalRepository;
        _engine = engine;
        _ledgerService = ledgerService;
    }

    public async Task<ServiceResponse<RerouteGuidanceResponse>> GetGuidanceAsync(
        RerouteGuidanceRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            return ServiceResponse<RerouteGuidanceResponse>.Fail(
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
            return ServiceResponse<RerouteGuidanceResponse>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "No routing hint available."),
                statusCode: 503,
                message: "No routing hint available");
        }

        var currentInterface = request.CurrentInterface?.Trim().ToLowerInvariant();
        var preferredInterface = hint.PreferredInterface.ToString().ToLowerInvariant();
        var rerouteRequired = !string.IsNullOrWhiteSpace(currentInterface)
            && !string.Equals(currentInterface, preferredInterface, StringComparison.OrdinalIgnoreCase);

        var response = new RerouteGuidanceResponse
        {
            NodeId = hint.NodeId,
            RerouteRequired = rerouteRequired,
            PreferredInterface = preferredInterface,
            FallbackInterfaces = hint.FallbackInterfaces.Select(i => i.ToString().ToLowerInvariant()).ToList(),
            Confidence = hint.Confidence,
            Reason = hint.Reason,
            GeneratedUtc = DateTimeOffset.UtcNow
        };

        await _eventRepository.AppendAsync(new MeshEvent
        {
            NodeId = hint.NodeId,
            Interface = hint.PreferredInterface,
            EventType = MeshEventType.RerouteSuggested,
            Reason = hint.Reason,
            CreatedUtc = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);

        await _ledgerService.AppendAsync("reroute.guidance", new
        {
            node_id = response.NodeId,
            current_interface = currentInterface,
            preferred_interface = response.PreferredInterface,
            fallback_interfaces = response.FallbackInterfaces,
            reroute_required = response.RerouteRequired,
            confidence = response.Confidence,
            reason = response.Reason,
            task_complexity = complexity.ToString().ToLowerInvariant(),
            survival_mode = survival.Enabled,
            observed_utc = response.GeneratedUtc
        }, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<RerouteGuidanceResponse>.Success(response);
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
