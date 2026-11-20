using Cwi.Common;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class TopologyHealthService
{
    private readonly IMeshTopologyRepository _repository;
    private readonly TopologyHealthOptions _options;

    public TopologyHealthService(
        IMeshTopologyRepository repository,
        TopologyHealthOptions options)
    {
        _repository = repository;
        _options = options;
    }

    public async Task<ServiceResponse<TopologyHealthResponse>> GetHealthAsync(CancellationToken cancellationToken)
    {
        var states = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var nodes = states
            .GroupBy(state => state.NodeId)
            .Select(group =>
            {
                var linkCount = group.Count();
                var degraded = group.Count(state => state.Status == LinkStateStatus.Degraded);
                var down = group.Count(state => state.Status == LinkStateStatus.Down);
                var status = ResolveNodeStatus(linkCount, degraded, down);
                return new NodeHealthResponse
                {
                    NodeId = group.Key,
                    Status = status,
                    LinkCount = linkCount,
                    DegradedLinks = degraded,
                    DownLinks = down
                };
            })
            .OrderBy(node => node.NodeId)
            .ToList();

        var response = new TopologyHealthResponse
        {
            Status = ResolveOverallStatus(nodes),
            TotalNodes = nodes.Count,
            DegradedNodes = nodes.Count(node => node.Status == "degraded"),
            DownNodes = nodes.Count(node => node.Status == "down"),
            TotalLinks = states.Count,
            DegradedLinks = states.Count(state => state.Status == LinkStateStatus.Degraded),
            DownLinks = states.Count(state => state.Status == LinkStateStatus.Down),
            Nodes = nodes,
            GeneratedUtc = DateTimeOffset.UtcNow
        };

        return ServiceResponse<TopologyHealthResponse>.Success(response);
    }

    private string ResolveNodeStatus(int linkCount, int degraded, int down)
    {
        if (linkCount == 0)
        {
            return "unknown";
        }

        var downRatio = (double)down / linkCount;
        var degradedRatio = (double)(down + degraded) / linkCount;

        if (downRatio >= ClampRatio(_options.DownLinkRatioThreshold, 1.0))
        {
            return "down";
        }

        if (degradedRatio >= ClampRatio(_options.DegradedLinkRatioThreshold, 0.01))
        {
            return "degraded";
        }

        return "healthy";
    }

    private string ResolveOverallStatus(IReadOnlyList<NodeHealthResponse> nodes)
    {
        if (nodes.Count == 0)
        {
            return "unknown";
        }

        var downRatio = nodes.Count(node => node.Status == "down") / (double)nodes.Count;
        var degradedRatio = nodes.Count(node => node.Status == "degraded") / (double)nodes.Count;

        if (downRatio >= ClampRatio(_options.OverallDownNodeRatioThreshold, 0.01))
        {
            return "down";
        }

        if (degradedRatio >= ClampRatio(_options.OverallDegradedNodeRatioThreshold, 0.01))
        {
            return "degraded";
        }

        return "healthy";
    }

    private static double ClampRatio(double value, double fallback)
    {
        if (value <= 0 || value > 1)
        {
            return fallback;
        }

        return value;
    }
}
