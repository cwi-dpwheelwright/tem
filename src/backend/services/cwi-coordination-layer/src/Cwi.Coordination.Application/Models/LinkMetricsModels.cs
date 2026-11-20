using System.Text.Json.Serialization;

namespace Cwi.Coordination.Application.Models;

public sealed class LinkMetricsTransportRequest
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("observed_utc")]
    public string? ObservedUtc { get; set; }

    [JsonPropertyName("sequence")]
    public long? Sequence { get; set; }

    [JsonPropertyName("payload")]
    public LinkMetricsReport? Payload { get; set; }

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    [JsonPropertyName("key_id")]
    public string? KeyId { get; set; }

    [JsonPropertyName("interfaces")]
    public IReadOnlyList<LinkMetricsInterface> Interfaces { get; set; } = Array.Empty<LinkMetricsInterface>();
}

public sealed class LinkMetricsReport
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("observed_utc")]
    public string? ObservedUtc { get; set; }

    [JsonPropertyName("interfaces")]
    public IReadOnlyList<LinkMetricsInterface> Interfaces { get; set; } = Array.Empty<LinkMetricsInterface>();
}

public sealed class LinkMetricsInterface
{
    [JsonPropertyName("interface_id")]
    public string InterfaceId { get; set; } = string.Empty;

    [JsonPropertyName("interface_type")]
    public string InterfaceType { get; set; } = string.Empty;

    [JsonPropertyName("observed_utc")]
    public string? ObservedUtc { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("rx_mbps")]
    public double? RxMbps { get; set; }

    [JsonPropertyName("tx_mbps")]
    public double? TxMbps { get; set; }

    [JsonPropertyName("mtu")]
    public int? Mtu { get; set; }

    [JsonPropertyName("quality")]
    public LinkMetricsQuality? Quality { get; set; }
}

public sealed class LinkMetricsQuality
{
    [JsonPropertyName("latency_ms")]
    public double? LatencyMs { get; set; }

    [JsonPropertyName("jitter_ms")]
    public double? JitterMs { get; set; }

    [JsonPropertyName("loss_ratio")]
    public double? LossRatio { get; set; }

    [JsonPropertyName("throughput_mbps")]
    public double? ThroughputMbps { get; set; }

    [JsonPropertyName("rssi_dbm")]
    public double? RssiDbm { get; set; }

    [JsonPropertyName("snr_db")]
    public double? SnrDb { get; set; }
}

public sealed class LinkMetricsIngestResponse
{
    [JsonPropertyName("node_id")]
    public string NodeId { get; set; } = string.Empty;

    [JsonPropertyName("accepted")]
    public int Accepted { get; set; }

    [JsonPropertyName("rejected")]
    public int Rejected { get; set; }
}
