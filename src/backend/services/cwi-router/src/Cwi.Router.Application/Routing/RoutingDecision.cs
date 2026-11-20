using Cwi.Router.Application.Health;

namespace Cwi.Router.Application.Routing;

public sealed class RoutingDecision
{
    public string DecisionId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public ComplexityDescriptor Complexity { get; init; } = new();
    public ModelTier RequestedTier { get; init; } = ModelTier.Free;
    public ModelTier SelectedTier { get; init; } = ModelTier.Free;
    public Uri? SelectedEndpoint { get; init; }
    public ModelEndpointCapabilities? EndpointCapabilities { get; init; }
    public bool FallbackUsed { get; init; }
    public double Score { get; init; }
    public string SurvivalMode { get; init; } = "normal";
    public NetworkAssessmentSnapshot? NetworkAssessment { get; init; }
    public bool RerouteRequired { get; init; }
    public string? RerouteReason { get; init; }
    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();
}
