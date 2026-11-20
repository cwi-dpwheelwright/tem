namespace Cwi.Router.Application.Health;

public enum InferenceEngineMode
{
    Unknown,
    Local,
    Remote
}

public sealed class ModelEndpointCapabilities
{
    public string? Engine { get; init; }
    public string? Tier { get; init; }
    public bool Ready { get; init; } = true;
    public int? MaxConcurrency { get; init; }
    public int? QueueDepth { get; init; }
    public InferenceEngineMode InferenceMode { get; init; } = InferenceEngineMode.Unknown;
}
