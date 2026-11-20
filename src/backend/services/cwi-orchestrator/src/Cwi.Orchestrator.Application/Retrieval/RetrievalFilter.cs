namespace Cwi.Orchestrator.Application.Retrieval;

public sealed class RetrievalFilter
{
    public string? PackId { get; init; }

    public string? PackHash { get; init; }

    public string? PackVersion { get; init; }
}
