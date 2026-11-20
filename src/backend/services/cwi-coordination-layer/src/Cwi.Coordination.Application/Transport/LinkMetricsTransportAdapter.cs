using Cwi.Coordination.Application.Models;

namespace Cwi.Coordination.Application.Transport;

public interface ILinkMetricsTransportAdapter
{
    Task<LinkMetricsTransportRequest> BuildSampleAsync(string nodeId, CancellationToken cancellationToken);
}

public sealed class SimulatedLinkMetricsTransportAdapter : ILinkMetricsTransportAdapter
{
    public Task<LinkMetricsTransportRequest> BuildSampleAsync(string nodeId, CancellationToken cancellationToken)
    {
        var observed = DateTimeOffset.UtcNow.ToString("O");
        var interfaces = new List<LinkMetricsInterface>
        {
            new()
            {
                InterfaceId = "wifi0",
                InterfaceType = "wifi",
                ObservedUtc = observed,
                State = "up",
                RxMbps = 12.5,
                TxMbps = 3.2,
                Mtu = 1500,
                Quality = new LinkMetricsQuality
                {
                    LatencyMs = 30,
                    JitterMs = 5,
                    LossRatio = 0.01,
                    ThroughputMbps = 12.0,
                    RssiDbm = -50,
                    SnrDb = 25
                }
            },
            new()
            {
                InterfaceId = "cell0",
                InterfaceType = "cellular",
                ObservedUtc = observed,
                State = "up",
                RxMbps = 5.1,
                TxMbps = 1.8,
                Mtu = 1420,
                Quality = new LinkMetricsQuality
                {
                    LatencyMs = 80,
                    JitterMs = 10,
                    LossRatio = 0.05,
                    ThroughputMbps = 5.0,
                    RssiDbm = -70,
                    SnrDb = 10
                }
            },
            new()
            {
                InterfaceId = "sat0",
                InterfaceType = "satellite",
                ObservedUtc = observed,
                State = "up",
                RxMbps = 1.2,
                TxMbps = 0.6,
                Mtu = 1280,
                Quality = new LinkMetricsQuality
                {
                    LatencyMs = 600,
                    JitterMs = 50,
                    LossRatio = 0.1,
                    ThroughputMbps = 1.0,
                    RssiDbm = -85,
                    SnrDb = 5
                }
            }
        };

        return Task.FromResult(new LinkMetricsTransportRequest
        {
            NodeId = nodeId,
            ObservedUtc = observed,
            Interfaces = interfaces
        });
    }
}
