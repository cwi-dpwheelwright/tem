namespace Cwi.Orchestrator.Application.Simulation;

public sealed class SimulationFeasibilityRequest
{
    public SimulationFeasibilityRequest(
        string actionType,
        IReadOnlyDictionary<string, object?> payload,
        TimeSpan timeBudget)
    {
        ActionType = actionType;
        Payload = payload;
        TimeBudget = timeBudget;
    }

    public string ActionType { get; }
    public IReadOnlyDictionary<string, object?> Payload { get; }
    public TimeSpan TimeBudget { get; }
}
