namespace Cwi.Router.Application.Routing;

public enum ComplexityLevel
{
    Low,
    Medium,
    High
}

public sealed class ComplexityDescriptor
{
    public string Version { get; init; } = "v1";
    public string RequestKind { get; init; } = "unknown";
    public int WordCount { get; init; }
    public int CharacterCount { get; init; }
    public int PayloadBytes { get; init; }
    public int? TopK { get; init; }
    public string? ActionType { get; init; }
    public double TokenEstimate { get; init; }
    public double Score { get; init; }
    public ComplexityLevel Level { get; init; }
}
