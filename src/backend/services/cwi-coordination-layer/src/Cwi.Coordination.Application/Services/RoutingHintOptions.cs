using Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Application.Services;

public sealed class RoutingHintOptions
{
    public int DefaultTtlSeconds { get; init; } = 60;
    public int CooldownSeconds { get; init; } = 30;
    public double HysteresisMargin { get; init; } = 0.1;
    public double LatencyNormalizationMs { get; init; } = 1500;
    public double JitterNormalizationMs { get; init; } = 200;
    public double BandwidthNormalizationKbps { get; init; } = 10000;
    public double ThroughputNormalizationKbps { get; init; } = 10000;

    public RoutingHintWeights LowComplexity { get; init; } = RoutingHintWeights.LowDefaults();
    public RoutingHintWeights MediumComplexity { get; init; } = RoutingHintWeights.MediumDefaults();
    public RoutingHintWeights HighComplexity { get; init; } = RoutingHintWeights.HighDefaults();

    public IReadOnlyDictionary<string, double> InterfaceWeights { get; init; } =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    public RoutingHintWeights ResolveWeights(TaskComplexity complexity) =>
        complexity switch
        {
            TaskComplexity.Low => LowComplexity,
            TaskComplexity.High => HighComplexity,
            _ => MediumComplexity
        };

    public double InterfaceWeight(InterfaceKind kind)
    {
        var key = kind.ToString().ToLowerInvariant();
        return InterfaceWeights.TryGetValue(key, out var weight) ? weight : 1.0;
    }
}

public sealed class RoutingHintWeights
{
    public double SignalWeight { get; init; } = 0.4;
    public double LossWeight { get; init; } = 0.3;
    public double LatencyWeight { get; init; } = 0.15;
    public double BandwidthWeight { get; init; } = 0.15;
    public double JitterWeight { get; init; } = 0.05;
    public double ThroughputWeight { get; init; } = 0.1;

    public static RoutingHintWeights LowDefaults() => new()
    {
        SignalWeight = 0.5,
        LossWeight = 0.3,
        LatencyWeight = 0.1,
        BandwidthWeight = 0.1,
        JitterWeight = 0.05,
        ThroughputWeight = 0.05
    };

    public static RoutingHintWeights MediumDefaults() => new();

    public static RoutingHintWeights HighDefaults() => new()
    {
        SignalWeight = 0.2,
        LossWeight = 0.2,
        LatencyWeight = 0.3,
        BandwidthWeight = 0.2,
        JitterWeight = 0.1,
        ThroughputWeight = 0.2
    };
}
