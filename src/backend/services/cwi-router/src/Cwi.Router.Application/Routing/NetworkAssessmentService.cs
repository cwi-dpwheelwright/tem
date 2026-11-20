using Cwi.Router.Application.Clients;
using Cwi.Router.Application.Health;
using Cwi.Router.Application.Pipeline;
using Microsoft.Extensions.Logging;

namespace Cwi.Router.Application.Routing;

public sealed class NetworkAssessmentService
{
    private readonly NetworkAssessmentOptions _options;
    private readonly NetworkAssessmentScorer _scorer;
    private readonly MeshAssessmentClient _meshClient;
    private readonly MeshHealthCache _meshCache;
    private readonly ILogger<NetworkAssessmentService> _logger;

    public NetworkAssessmentService(
        NetworkAssessmentOptions options,
        NetworkAssessmentScorer scorer,
        MeshAssessmentClient meshClient,
        MeshHealthCache meshCache,
        ILogger<NetworkAssessmentService> logger)
    {
        _options = options;
        _scorer = scorer;
        _meshClient = meshClient;
        _meshCache = meshCache;
        _logger = logger;
    }

    public async Task<NetworkAssessmentSnapshot?> EvaluateAsync(
        RouterRequestContext context,
        ComplexityDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return null;
        }

        if (!NetworkAssessmentParser.TryParse(context.Body, out var input))
        {
            return null;
        }

        var nodeId = input.NodeId;
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            nodeId = context.HttpContext.Request.Headers["x-node-id"].ToString();
        }

        input = new NetworkAssessmentInput
        {
            NodeId = nodeId,
            ObservedUtc = input.ObservedUtc,
            Interfaces = input.Interfaces
        };

        var score = _scorer.Score(input);
        var meshHealth = await LoadMeshHealthAsync(cancellationToken).ConfigureAwait(false);
        var hint = await LoadHintAsync(nodeId, descriptor, cancellationToken).ConfigureAwait(false);

        var preferredInterface = !string.IsNullOrWhiteSpace(hint?.PreferredInterface)
            ? hint.PreferredInterface
            : score.PreferredInterface;

        var rerouteReasons = new List<string>();
        var rerouteRequired = false;

        if (string.Equals(score.Status, "down", StringComparison.OrdinalIgnoreCase))
        {
            rerouteRequired = true;
            rerouteReasons.Add("network_down");
        }
        else if (string.Equals(score.Status, "degraded", StringComparison.OrdinalIgnoreCase) &&
            _options.TriggerRerouteOnDegraded)
        {
            rerouteRequired = true;
            rerouteReasons.Add("network_degraded");
        }

        if (meshHealth is not null)
        {
            if (string.Equals(meshHealth.Status, "down", StringComparison.OrdinalIgnoreCase))
            {
                rerouteRequired = true;
                rerouteReasons.Add("mesh_down");
            }
            else if (string.Equals(meshHealth.Status, "degraded", StringComparison.OrdinalIgnoreCase) &&
                _options.TriggerRerouteOnDegraded)
            {
                rerouteRequired = true;
                rerouteReasons.Add("mesh_degraded");
            }
        }

        return new NetworkAssessmentSnapshot
        {
            Status = score.Status,
            Score = score.Score,
            PreferredInterface = preferredInterface,
            NodeId = nodeId,
            RerouteRequired = rerouteRequired,
            RerouteReason = rerouteReasons.Count == 0 ? null : string.Join('|', rerouteReasons),
            MeshHealth = meshHealth,
            RoutingHint = hint
        };
    }

    private async Task<MeshHealthSnapshot?> LoadMeshHealthAsync(CancellationToken cancellationToken)
    {
        if (!_options.MeshHealthEnabled)
        {
            return null;
        }

        try
        {
            return await _meshCache.GetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "mesh health lookup failed");
            return null;
        }
    }

    private async Task<MeshRoutingHint?> LoadHintAsync(
        string? nodeId,
        ComplexityDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        if (!_options.HintEnabled || string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.MeshHintTimeoutSeconds));

            var complexity = descriptor.Level.ToString().ToLowerInvariant();
            return await _meshClient.GetHintAsync(nodeId, complexity, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "mesh hint lookup failed");
            return null;
        }
    }
}
