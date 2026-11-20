using Cwi.Router.Application.Health;

namespace Cwi.Router.Application.Routing;

public sealed class NetworkAssessmentInput
{
    public string? NodeId { get; init; }
    public DateTimeOffset? ObservedUtc { get; init; }
    public IReadOnlyList<NetworkInterfaceSignal> Interfaces { get; init; } = Array.Empty<NetworkInterfaceSignal>();
}

public sealed class NetworkInterfaceSignal
{
    public string Interface { get; init; } = string.Empty;
    public string? State { get; init; }
    public double? SignalQuality { get; init; }
    public double? LatencyMs { get; init; }
    public double? JitterMs { get; init; }
    public double? PacketLoss { get; init; }
    public double? ThroughputMbps { get; init; }
    public double? BandwidthMbps { get; init; }
    public double? RssiDbm { get; init; }
    public double? SnrDb { get; init; }
}

public sealed class NetworkAssessmentSnapshot
{
    public string Status { get; init; } = "unknown";
    public double? Score { get; init; }
    public string? PreferredInterface { get; init; }
    public string? NodeId { get; init; }
    public bool RerouteRequired { get; init; }
    public string? RerouteReason { get; init; }
    public MeshHealthSnapshot? MeshHealth { get; init; }
    public MeshRoutingHint? RoutingHint { get; init; }
}

public sealed class MeshRoutingHint
{
    public string PreferredInterface { get; init; } = string.Empty;
    public string? PreferredPath { get; init; }
    public double Confidence { get; init; }
    public string Reason { get; init; } = string.Empty;
    public IReadOnlyList<string> FallbackInterfaces { get; init; } = Array.Empty<string>();
}
