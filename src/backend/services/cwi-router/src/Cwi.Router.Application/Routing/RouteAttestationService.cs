using System.Text.Json;
using System.Text.Json.Serialization;
using Cwi.Router.Application.Pipeline;

namespace Cwi.Router.Application.Routing;

public sealed class RouteAttestationService
{
    private readonly RouteAttestationReplayStore _store;
    private readonly RouteAttestationValidator _validator;
    private readonly JsonSerializerOptions _serializerOptions;

    public RouteAttestationService(RouteAttestationReplayStore store, RouteAttestationValidator validator)
    {
        _store = store;
        _validator = validator;
        _serializerOptions = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public RouteAttestationEnvelope Create(RoutingDecision decision, RouterRequestContext context)
    {
        var traceId = context.HttpContext.TraceIdentifier ?? string.Empty;
        var nodeId = decision.NetworkAssessment?.NodeId;
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            nodeId = context.HttpContext.Request.Headers["x-node-id"].ToString();
        }

        var key = ResolveKey(nodeId, context.TenantId, traceId);
        var sequence = _store.NextSequence(key);
        var nonce = Guid.NewGuid().ToString("N");

        var envelope = new RouteAttestationEnvelope
        {
            TraceId = traceId,
            TenantId = context.TenantId,
            NodeId = string.IsNullOrWhiteSpace(nodeId) ? null : nodeId,
            DecisionId = decision.DecisionId,
            SelectedTier = decision.SelectedTier.ToString().ToLowerInvariant(),
            SelectedEndpoint = decision.SelectedEndpoint?.ToString(),
            NetworkStatus = decision.NetworkAssessment?.Status,
            NetworkScore = decision.NetworkAssessment?.Score,
            MeshHealthStatus = decision.NetworkAssessment?.MeshHealth?.Status,
            PreferredInterface = decision.NetworkAssessment?.PreferredInterface,
            RerouteRequired = decision.RerouteRequired,
            RerouteReason = decision.RerouteReason,
            ReasonCodes = decision.ReasonCodes,
            Nonce = nonce,
            Sequence = sequence
        };

        _validator.TryValidate(envelope, out _);
        return envelope;
    }

    public string Serialize(RouteAttestationEnvelope envelope)
    {
        return JsonSerializer.Serialize(envelope, _serializerOptions);
    }

    private static string ResolveKey(string? nodeId, string? tenantId, string traceId)
    {
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            return nodeId;
        }

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            return tenantId;
        }

        if (!string.IsNullOrWhiteSpace(traceId))
        {
            return traceId;
        }

        return "unknown";
    }
}
