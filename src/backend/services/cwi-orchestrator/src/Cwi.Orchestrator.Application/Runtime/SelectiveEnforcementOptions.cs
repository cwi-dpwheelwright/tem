#nullable enable
using System;

namespace Cwi.Orchestrator.Application.Runtime;

public sealed class SelectiveEnforcementOptions
{
    public SelectiveEnforcementMode Critic { get; init; } = SelectiveEnforcementMode.Observe;
    public SelectiveEnforcementMode Truth { get; init; } = SelectiveEnforcementMode.Observe;
    public SelectiveEnforcementMode CacheServing { get; init; } = SelectiveEnforcementMode.Observe;
    public SelectiveEnforcementMode ExternalOpinion { get; init; } = SelectiveEnforcementMode.Observe;
    public SelectiveEnforcementMode FederatedSummaries { get; init; } = SelectiveEnforcementMode.Observe;

    public bool IsEnabled(SelectiveEnforcementFeature feature)
    {
        return Resolve(feature) != SelectiveEnforcementMode.Disabled;
    }

    public bool IsEnforced(SelectiveEnforcementFeature feature)
    {
        return Resolve(feature) == SelectiveEnforcementMode.Enforce;
    }

    public SelectiveEnforcementMode Resolve(SelectiveEnforcementFeature feature)
    {
        return feature switch
        {
            SelectiveEnforcementFeature.Critic => Critic,
            SelectiveEnforcementFeature.Truth => Truth,
            SelectiveEnforcementFeature.CacheServing => CacheServing,
            SelectiveEnforcementFeature.ExternalOpinion => ExternalOpinion,
            SelectiveEnforcementFeature.FederatedSummaries => FederatedSummaries,
            _ => SelectiveEnforcementMode.Observe
        };
    }

    public static SelectiveEnforcementOptions LoadFromEnvironment()
    {
        return new SelectiveEnforcementOptions
        {
            Critic = ReadMode("CWI_CRITIC_ENFORCEMENT_MODE"),
            Truth = ReadMode("CWI_TRUTH_ENFORCEMENT_MODE"),
            CacheServing = ReadMode("CWI_CACHE_SERVING_MODE"),
            ExternalOpinion = ReadMode("CWI_EXTERNAL_OPINION_MODE"),
            FederatedSummaries = ReadMode("CWI_FEDERATED_SUMMARY_MODE")
        };
    }

    private static SelectiveEnforcementMode ReadMode(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return SelectiveEnforcementMode.Observe;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "disabled" or "off" or "false" => SelectiveEnforcementMode.Disabled,
            "enforce" or "enforced" or "block" => SelectiveEnforcementMode.Enforce,
            _ => SelectiveEnforcementMode.Observe
        };
    }
}

public enum SelectiveEnforcementFeature
{
    Critic,
    Truth,
    CacheServing,
    ExternalOpinion,
    FederatedSummaries
}

public enum SelectiveEnforcementMode
{
    Disabled,
    Observe,
    Enforce
}
#nullable disable
