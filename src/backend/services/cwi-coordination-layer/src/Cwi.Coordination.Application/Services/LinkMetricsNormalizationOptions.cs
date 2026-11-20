namespace Cwi.Coordination.Application.Services;

public sealed class LinkMetricsNormalizationOptions
{
    public double SnrNormalizationDb { get; init; } = 30;
    public double RssiMinDbm { get; init; } = -100;
    public double RssiMaxDbm { get; init; } = -30;
    public double BandwidthNormalizationKbps { get; init; } = 10000;
    public double DefaultSignalQuality { get; init; } = 0.5;
}
