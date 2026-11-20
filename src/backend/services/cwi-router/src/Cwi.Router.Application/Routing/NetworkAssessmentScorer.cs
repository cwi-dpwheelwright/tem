namespace Cwi.Router.Application.Routing;

public sealed class NetworkAssessmentScorer
{
    private readonly NetworkAssessmentOptions _options;

    public NetworkAssessmentScorer(NetworkAssessmentOptions options)
    {
        _options = options;
    }

    public NetworkAssessmentScore Score(NetworkAssessmentInput input)
    {
        if (input.Interfaces.Count == 0)
        {
            return new NetworkAssessmentScore("unknown", null, null);
        }

        var best = input.Interfaces
            .Select(signal => new
            {
                Signal = signal,
                Score = ComputeScore(signal)
            })
            .OrderByDescending(entry => entry.Score)
            .First();

        var status = best.Score >= _options.HealthyThreshold
            ? "healthy"
            : best.Score >= _options.DegradedThreshold
                ? "degraded"
                : "down";

        return new NetworkAssessmentScore(status, best.Score, best.Signal.Interface);
    }

    private double ComputeScore(NetworkInterfaceSignal signal)
    {
        if (string.Equals(signal.State, "down", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var signalQuality = ResolveSignalQuality(signal);
        var packetLossScore = 1 - Clamp01(signal.PacketLoss ?? 0);
        var latencyScore = 1 - Clamp01((signal.LatencyMs ?? 0) / 1000.0);
        var throughputScore = Clamp01((signal.ThroughputMbps ?? signal.BandwidthMbps ?? 0) / 100.0);

        var score = (0.4 * signalQuality) +
            (0.2 * packetLossScore) +
            (0.2 * latencyScore) +
            (0.2 * throughputScore);

        return Clamp01(score);
    }

    private static double ResolveSignalQuality(NetworkInterfaceSignal signal)
    {
        if (signal.SignalQuality.HasValue)
        {
            return Clamp01(signal.SignalQuality.Value);
        }

        if (signal.SnrDb.HasValue)
        {
            return Clamp01(signal.SnrDb.Value / 30.0);
        }

        if (signal.RssiDbm.HasValue)
        {
            return Clamp01((signal.RssiDbm.Value + 100) / 70.0);
        }

        return 0.5;
    }

    private static double Clamp01(double value)
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

public sealed class NetworkAssessmentScore
{
    public string Status { get; }
    public double? Score { get; }
    public string? PreferredInterface { get; }

    public NetworkAssessmentScore(string status, double? score, string? preferredInterface)
    {
        Status = status;
        Score = score;
        PreferredInterface = preferredInterface;
    }
}
