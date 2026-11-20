using Cwi.Router.Application.Health;
using Cwi.Router.Application.Survival;

namespace Cwi.Router.Application.Routing;

public sealed class RoutingDecisionEngine
{
    private readonly ModelEndpointRegistry _registry;
    private readonly SurvivalStateStore _survivalStore;
    private readonly NetworkAssessmentOptions _networkOptions;

    public RoutingDecisionEngine(
        ModelEndpointRegistry registry,
        SurvivalStateStore survivalStore,
        NetworkAssessmentOptions networkOptions)
    {
        _registry = registry;
        _survivalStore = survivalStore;
        _networkOptions = networkOptions;
    }

    public RoutingDecision Decide(
        ComplexityDescriptor descriptor,
        ModelTier requestedTier,
        DateTimeOffset now,
        NetworkAssessmentSnapshot? networkAssessment)
    {
        var reasons = new List<string>();
        var survival = _survivalStore.GetSnapshot();
        var survivalMode = survival.Mode;

        var recommendedTier = requestedTier;
        if (survivalMode is "survival" or "degraded")
        {
            recommendedTier = ModelTier.Free;
            reasons.Add("survival_mode_bias");
        }
        else if (descriptor.Level == ComplexityLevel.High)
        {
            recommendedTier = ModelTier.Pro;
            reasons.Add("complexity_high");
        }
        else if (descriptor.Level == ComplexityLevel.Medium)
        {
            recommendedTier = ModelTier.Pro;
            reasons.Add("complexity_medium");
        }
        else
        {
            recommendedTier = ModelTier.Free;
            reasons.Add("complexity_low");
        }

        var selectedTier = recommendedTier;
        var endpoints = _registry.GetHealthyEndpoints(recommendedTier, now);
        var fallbackUsed = false;

        if (endpoints.Count == 0)
        {
            var fallbackTier = recommendedTier == ModelTier.Pro ? ModelTier.Free : ModelTier.Pro;
            endpoints = _registry.GetHealthyEndpoints(fallbackTier, now);
            if (endpoints.Count > 0)
            {
                selectedTier = fallbackTier;
                fallbackUsed = true;
                reasons.Add("tier_fallback");
            }
        }

        var endpoint = endpoints.Count > 0 ? endpoints[0] : null;
        ModelEndpointCapabilities? endpointCapabilities = null;
        if (endpoint is not null && _registry.TryGetCapabilities(endpoint, out var capabilities))
        {
            endpointCapabilities = capabilities;
            switch (capabilities.InferenceMode)
            {
                case InferenceEngineMode.Local:
                    reasons.Add("capability_local_inference");
                    break;
                case InferenceEngineMode.Remote:
                    reasons.Add("capability_remote_inference");
                    break;
            }
        }

        var adjustedScore = ApplyCapabilityBias(descriptor.Score, endpointCapabilities);
        adjustedScore = ApplyNetworkBias(adjustedScore, networkAssessment, reasons);

        return new RoutingDecision
        {
            Complexity = descriptor,
            RequestedTier = requestedTier,
            SelectedTier = selectedTier,
            SelectedEndpoint = endpoint,
            EndpointCapabilities = endpointCapabilities,
            FallbackUsed = fallbackUsed,
            Score = adjustedScore,
            SurvivalMode = survivalMode,
            NetworkAssessment = networkAssessment,
            RerouteRequired = networkAssessment?.RerouteRequired ?? false,
            RerouteReason = networkAssessment?.RerouteReason,
            ReasonCodes = reasons
        };
    }

    private static double ApplyCapabilityBias(double score, ModelEndpointCapabilities? capabilities)
    {
        if (capabilities is null)
        {
            return score;
        }

        var bias = capabilities.InferenceMode switch
        {
            InferenceEngineMode.Local => -0.05,
            InferenceEngineMode.Remote => 0.05,
            _ => 0.0
        };

        var adjusted = score + bias;
        if (adjusted < 0)
        {
            return 0;
        }

        if (adjusted > 1)
        {
            return 1;
        }

        return adjusted;
    }

    private double ApplyNetworkBias(
        double score,
        NetworkAssessmentSnapshot? assessment,
        List<string> reasons)
    {
        if (!_networkOptions.Enabled || assessment is null || !assessment.Score.HasValue)
        {
            return score;
        }

        var weight = _networkOptions.ScoreWeight;
        if (weight < 0)
        {
            weight = 0;
        }
        else if (weight > 1)
        {
            weight = 1;
        }

        var blended = (score * (1 - weight)) + (assessment.Score.Value * weight);
        var adjusted = blended < 0 ? 0 : blended > 1 ? 1 : blended;

        if (!string.IsNullOrWhiteSpace(assessment.Status))
        {
            reasons.Add($"network_{assessment.Status}");
        }

        if (assessment.RerouteRequired)
        {
            reasons.Add("reroute_required");
        }

        if (!string.IsNullOrWhiteSpace(assessment.RerouteReason))
        {
            reasons.Add($"reroute_{assessment.RerouteReason}");
        }

        if (!string.IsNullOrWhiteSpace(assessment.MeshHealth?.Status))
        {
            reasons.Add($"mesh_{assessment.MeshHealth.Status}");
        }

        if (!string.IsNullOrWhiteSpace(assessment.RoutingHint?.PreferredInterface))
        {
            reasons.Add("mesh_hint_preferred");
        }

        return adjusted;
    }
}
