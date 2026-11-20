namespace Cwi.Coordination.Application.Services;

public sealed class TopologyHealthOptions
{
    public double DownLinkRatioThreshold { get; init; } = 1.0;
    public double DegradedLinkRatioThreshold { get; init; } = 0.01;
    public double OverallDownNodeRatioThreshold { get; init; } = 0.01;
    public double OverallDegradedNodeRatioThreshold { get; init; } = 0.01;
}
