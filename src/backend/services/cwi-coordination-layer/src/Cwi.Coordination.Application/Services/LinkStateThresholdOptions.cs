namespace Cwi.Coordination.Application.Services;

public sealed class LinkStateThresholdOptions
{
    public double DownSignalQuality { get; init; } = 0.05;
    public double DownPacketLoss { get; init; } = 0.8;
    public int DownBandwidthKbps { get; init; } = 0;

    public double DegradedSignalQuality { get; init; } = 0.3;
    public double DegradedPacketLoss { get; init; } = 0.3;
    public int DegradedLatencyMs { get; init; } = 1200;
}
