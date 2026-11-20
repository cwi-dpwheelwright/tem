using Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Application.Services;

public interface IRoutingHintEngine
{
    RoutingHint? ComputeHint(
        string nodeId,
        IReadOnlyList<LinkState> states,
        TaskComplexity complexity,
        bool survivalModeEnabled);
}

public sealed class DefaultRoutingHintEngine : IRoutingHintEngine
{
    private readonly RoutingHintOptions _options;

    public DefaultRoutingHintEngine(RoutingHintOptions options)
    {
        _options = options;
    }

    public RoutingHint? ComputeHint(
        string nodeId,
        IReadOnlyList<LinkState> states,
        TaskComplexity complexity,
        bool survivalModeEnabled)
    {
        if (states.Count == 0)
        {
            return null;
        }

        var candidates = states
            .Where(state => state.Status != LinkStateStatus.Down)
            .ToList();

        if (survivalModeEnabled)
        {
            candidates = candidates
                .Where(state => state.PacketLoss <= 0.2 && state.LatencyMs <= 750)
                .ToList();
        }

        if (candidates.Count == 0)
        {
            candidates = states.Where(state => state.Status != LinkStateStatus.Down).ToList();
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        var weights = _options.ResolveWeights(complexity);
        var scored = candidates
            .Select(state => new
            {
                State = state,
                Score = ComputeScore(state, weights) * _options.InterfaceWeight(state.Interface)
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.State.Interface)
            .ToList();

        var selected = scored[0];
        var fallbacks = scored.Skip(1).Select(item => item.State.Interface).ToList();

        return new RoutingHint
        {
            NodeId = nodeId,
            PreferredInterface = selected.State.Interface,
            PreferredPath = selected.State.Interface.ToString().ToLowerInvariant(),
            Confidence = Math.Round(selected.Score, 4),
            Reason = $"Selected {selected.State.Interface} with score {selected.Score:F2}",
            FallbackInterfaces = fallbacks,
            TtlSeconds = _options.DefaultTtlSeconds,
            CooldownSeconds = _options.CooldownSeconds,
            HysteresisMargin = _options.HysteresisMargin
        };
    }

    private double ComputeScore(LinkState state, RoutingHintWeights weights)
    {
        var signal = Clamp(state.SignalQuality);
        var packetLossScore = 1.0 - Clamp(state.PacketLoss);
        var latencyScore = 1.0 - Clamp(state.LatencyMs / _options.LatencyNormalizationMs);
        var bandwidthScore = Clamp(state.BandwidthKbps / _options.BandwidthNormalizationKbps);
        var throughputScore = Clamp(state.ThroughputKbps / _options.ThroughputNormalizationKbps);
        var jitterScore = state.JitterMs.HasValue
            ? 1.0 - Clamp(state.JitterMs.Value / _options.JitterNormalizationMs)
            : 1.0;

        return weights.SignalWeight * signal +
            weights.LossWeight * packetLossScore +
            weights.LatencyWeight * latencyScore +
            weights.BandwidthWeight * bandwidthScore +
            weights.JitterWeight * jitterScore +
            weights.ThroughputWeight * throughputScore;
    }

    private static double Clamp(double value)
    {
        if (value < 0)
        {
            return 0;
        }
        if (value > 1)
        {
            return 1;
        }
        return value;
    }
}
