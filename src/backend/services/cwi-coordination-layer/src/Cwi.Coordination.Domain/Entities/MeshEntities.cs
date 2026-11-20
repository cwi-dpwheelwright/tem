namespace Cwi.Coordination.Domain.Entities;

public enum InterfaceKind
{
    Unknown,
    Wifi,
    Cellular,
    Ethernet,
    Satellite,
    LoRa
}

public enum LinkStateStatus
{
    Healthy,
    Degraded,
    Down
}

public enum MeshEventType
{
    LinkDegraded,
    LinkRecovered,
    LinkDown,
    RerouteSuggested
}

public enum TaskComplexity
{
    Low,
    Medium,
    High
}

public sealed class LinkState
{
    public string NodeId { get; set; } = string.Empty;
    public InterfaceKind Interface { get; set; } = InterfaceKind.Unknown;
    public double SignalQuality { get; set; }
    public int LatencyMs { get; set; }
    public double? JitterMs { get; set; }
    public double PacketLoss { get; set; }
    public int BandwidthKbps { get; set; }
    public int ThroughputKbps { get; set; }
    public double? RssiDbm { get; set; }
    public double? SnrDb { get; set; }
    public LinkStateStatus Status { get; set; } = LinkStateStatus.Healthy;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class RoutingHint
{
    public string NodeId { get; set; } = string.Empty;
    public InterfaceKind PreferredInterface { get; set; } = InterfaceKind.Unknown;
    public string? PreferredPath { get; set; }
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
    public IReadOnlyList<InterfaceKind> FallbackInterfaces { get; set; } = Array.Empty<InterfaceKind>();
    public int TtlSeconds { get; set; }
    public int CooldownSeconds { get; set; }
    public double HysteresisMargin { get; set; }
}

public sealed class BackhaulPolicy
{
    public bool RequireAuthenticatedChannel { get; set; } = true;
    public IReadOnlyList<InterfaceKind> AllowedInterfaces { get; set; } = Array.Empty<InterfaceKind>();
    public IReadOnlyList<InterfaceKind> DeniedInterfaces { get; set; } = Array.Empty<InterfaceKind>();
    public IReadOnlyList<string> RevokedPeers { get; set; } = Array.Empty<string>();
    public string PolicyVersion { get; set; } = "1.0";
}

public sealed class SurvivalModeState
{
    public bool Enabled { get; set; }
    public string? Reason { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MeshEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string NodeId { get; set; } = string.Empty;
    public InterfaceKind Interface { get; set; } = InterfaceKind.Unknown;
    public MeshEventType EventType { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
