using System.Security.Cryptography;
using System.Text;

namespace Cwi.Router.Application.Routing;

public sealed class RoutingDecisionEvent
{
    public string DecisionId { get; init; } = string.Empty;
    public string TraceId { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public string RequestKind { get; init; } = string.Empty;
    public string SelectedTier { get; init; } = string.Empty;
    public bool FallbackUsed { get; init; }
    public string SurvivalMode { get; init; } = string.Empty;
    public double Score { get; init; }
    public string? NetworkStatus { get; init; }
    public double? NetworkScore { get; init; }
    public string? NetworkPreferredInterface { get; init; }
    public string? MeshHealthStatus { get; init; }
    public string? MeshHintInterface { get; init; }
    public double? MeshHintConfidence { get; init; }
    public bool RerouteRequired { get; init; }
    public string? RerouteReason { get; init; }
    public string ComplexityLevel { get; init; } = string.Empty;
    public int WordCount { get; init; }
    public int PayloadBytes { get; init; }
    public string? ActionType { get; init; }
    public string? EndpointEngine { get; init; }
    public string? EndpointInferenceMode { get; init; }
    public string? EndpointTier { get; init; }
    public bool? EndpointReady { get; init; }
    public int? EndpointMaxConcurrency { get; init; }
    public int? EndpointQueueDepth { get; init; }
    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();
    public DateTimeOffset TimestampUtc { get; init; }

    public static RoutingDecisionEvent FromDecision(
        RoutingDecision decision,
        string traceId,
        string tenantId)
    {
        return new RoutingDecisionEvent
        {
            DecisionId = decision.DecisionId,
            TraceId = traceId,
            TenantId = HashValue(tenantId),
            RequestKind = decision.Complexity.RequestKind,
            SelectedTier = decision.SelectedTier.ToString().ToLowerInvariant(),
            FallbackUsed = decision.FallbackUsed,
            SurvivalMode = decision.SurvivalMode,
            Score = decision.Score,
            NetworkStatus = decision.NetworkAssessment?.Status,
            NetworkScore = decision.NetworkAssessment?.Score,
            NetworkPreferredInterface = decision.NetworkAssessment?.PreferredInterface,
            MeshHealthStatus = decision.NetworkAssessment?.MeshHealth?.Status,
            MeshHintInterface = decision.NetworkAssessment?.RoutingHint?.PreferredInterface,
            MeshHintConfidence = decision.NetworkAssessment?.RoutingHint?.Confidence,
            RerouteRequired = decision.RerouteRequired,
            RerouteReason = decision.RerouteReason,
            ComplexityLevel = decision.Complexity.Level.ToString().ToLowerInvariant(),
            WordCount = decision.Complexity.WordCount,
            PayloadBytes = decision.Complexity.PayloadBytes,
            ActionType = decision.Complexity.ActionType,
            EndpointEngine = decision.EndpointCapabilities?.Engine,
            EndpointInferenceMode = decision.EndpointCapabilities is null
                ? null
                : decision.EndpointCapabilities.InferenceMode.ToString().ToLowerInvariant(),
            EndpointTier = decision.EndpointCapabilities?.Tier,
            EndpointReady = decision.EndpointCapabilities?.Ready,
            EndpointMaxConcurrency = decision.EndpointCapabilities?.MaxConcurrency,
            EndpointQueueDepth = decision.EndpointCapabilities?.QueueDepth,
            ReasonCodes = decision.ReasonCodes,
            TimestampUtc = decision.TimestampUtc
        };
    }

    private static string HashValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value.Trim()));
        var output = BitConverter.ToString(bytes).Replace("-", string.Empty);
        return output.Length > 12 ? output.Substring(0, 12) : output;
    }
}
