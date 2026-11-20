using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application.Models;

namespace Cwi.Coordination.Application.Services;

public sealed class LinkMetricsIngestService
{
    private readonly MeshTopologyService _topologyService;
    private readonly LinkMetricsNormalizationOptions _options;

    public LinkMetricsIngestService(
        MeshTopologyService topologyService,
        LinkMetricsNormalizationOptions options)
    {
        _topologyService = topologyService;
        _options = options;
    }

    public async Task<ServiceResponse<LinkMetricsIngestResponse>> IngestAsync(
        LinkMetricsTransportRequest request,
        CancellationToken cancellationToken)
    {
        var report = NormalizeReport(request);
        if (string.IsNullOrWhiteSpace(report.NodeId))
        {
            return ServiceResponse<LinkMetricsIngestResponse>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "node_id required."),
                statusCode: 400,
                message: "node_id required");
        }

        if (report.Interfaces.Count == 0)
        {
            return ServiceResponse<LinkMetricsIngestResponse>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "interfaces required."),
                statusCode: 400,
                message: "interfaces required");
        }

        var accepted = 0;
        var rejected = 0;
        foreach (var iface in report.Interfaces)
        {
            var linkRequest = MapLinkState(report.NodeId, iface);
            var response = await _topologyService.UpsertAsync(linkRequest, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccess)
            {
                accepted += 1;
            }
            else
            {
                rejected += 1;
            }
        }

        return ServiceResponse<LinkMetricsIngestResponse>.Success(new LinkMetricsIngestResponse
        {
            NodeId = report.NodeId,
            Accepted = accepted,
            Rejected = rejected
        });
    }

    private static LinkMetricsReport NormalizeReport(LinkMetricsTransportRequest request)
    {
        if (request.Payload is not null)
        {
            return request.Payload;
        }

        return new LinkMetricsReport
        {
            NodeId = request.NodeId,
            ObservedUtc = request.ObservedUtc,
            Interfaces = request.Interfaces
        };
    }

    private LinkStateUpdateRequest MapLinkState(string nodeId, LinkMetricsInterface iface)
    {
        var quality = iface.Quality ?? new LinkMetricsQuality();
        var latencyMs = quality.LatencyMs.HasValue ? (int)Math.Round(quality.LatencyMs.Value) : 0;
        var jitterMs = quality.JitterMs;
        var packetLoss = ClampRatio(quality.LossRatio ?? 0);
        var bandwidthKbps = ComputeBandwidthKbps(iface, quality);
        var throughputKbps = ComputeThroughputKbps(quality);
        var signalQuality = ComputeSignalQuality(iface, quality, bandwidthKbps);

        return new LinkStateUpdateRequest
        {
            NodeId = nodeId,
            Interface = ResolveInterface(iface),
            SignalQuality = signalQuality,
            LatencyMs = latencyMs,
            JitterMs = jitterMs,
            PacketLoss = packetLoss,
            BandwidthKbps = bandwidthKbps,
            ThroughputKbps = throughputKbps,
            RssiDbm = quality.RssiDbm,
            SnrDb = quality.SnrDb
        };
    }

    private static string ResolveInterface(LinkMetricsInterface iface)
    {
        if (!string.IsNullOrWhiteSpace(iface.InterfaceType))
        {
            return iface.InterfaceType;
        }
        if (!string.IsNullOrWhiteSpace(iface.InterfaceId))
        {
            return iface.InterfaceId;
        }
        return "unknown";
    }

    private static int ComputeBandwidthKbps(LinkMetricsInterface iface, LinkMetricsQuality quality)
    {
        var throughput = quality.ThroughputMbps;
        if (throughput.HasValue)
        {
            return (int)Math.Round(Math.Max(0, throughput.Value) * 1000);
        }

        var rx = iface.RxMbps ?? 0;
        var tx = iface.TxMbps ?? 0;
        var max = Math.Max(rx, tx);
        if (max <= 0)
        {
            return 0;
        }
        return (int)Math.Round(max * 1000);
    }

    private static int ComputeThroughputKbps(LinkMetricsQuality quality)
    {
        var throughput = quality.ThroughputMbps;
        if (!throughput.HasValue)
        {
            return 0;
        }
        return (int)Math.Round(Math.Max(0, throughput.Value) * 1000);
    }

    private double ComputeSignalQuality(LinkMetricsInterface iface, LinkMetricsQuality quality, int bandwidthKbps)
    {
        if (iface.State.Trim().Equals("down", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (quality.SnrDb.HasValue)
        {
            var divisor = _options.SnrNormalizationDb <= 0 ? 30.0 : _options.SnrNormalizationDb;
            return Clamp01(quality.SnrDb.Value / divisor);
        }

        if (quality.RssiDbm.HasValue)
        {
            var span = _options.RssiMaxDbm - _options.RssiMinDbm;
            if (span <= 0)
            {
                span = 70.0;
            }
            return Clamp01((quality.RssiDbm.Value - _options.RssiMinDbm) / span);
        }

        if (bandwidthKbps > 0)
        {
            var divisor = _options.BandwidthNormalizationKbps <= 0 ? 10000.0 : _options.BandwidthNormalizationKbps;
            return Clamp01(bandwidthKbps / divisor);
        }

        return Clamp01(_options.DefaultSignalQuality);
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

    private static double ClampRatio(double value)
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
