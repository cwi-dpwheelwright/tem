namespace Cwi.Orchestrator.Api.Models;

public sealed class ReadyResponse
{
    public bool Ready { get; init; }
    public Dictionary<string, ReadyCheck> Checks { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; }
}

public sealed class ReadyCheck
{
    public string Status { get; init; } = "ok";
    public string? Detail { get; init; }
}
