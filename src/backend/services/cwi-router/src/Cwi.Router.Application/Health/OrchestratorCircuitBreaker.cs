using System.Collections.Concurrent;

namespace Cwi.Router.Application.Health;

public sealed class OrchestratorCircuitBreaker
{
    private readonly ConcurrentDictionary<string, CircuitState> _states = new(StringComparer.OrdinalIgnoreCase);

    public bool IsOpen(string endpoint, DateTimeOffset now)
    {
        var state = _states.GetOrAdd(endpoint, _ => new CircuitState());
        return state.OpenUntil.HasValue && state.OpenUntil > now;
    }

    public void RecordSuccess(string endpoint)
    {
        var state = _states.GetOrAdd(endpoint, _ => new CircuitState());
        state.Failures = 0;
        state.OpenUntil = null;
    }

    public void RecordFailure(string endpoint, int threshold, TimeSpan cooldown)
    {
        var state = _states.GetOrAdd(endpoint, _ => new CircuitState());
        state.Failures++;
        if (state.Failures >= threshold)
        {
            state.OpenUntil = DateTimeOffset.UtcNow.Add(cooldown);
        }
    }

    private sealed class CircuitState
    {
        public int Failures { get; set; }
        public DateTimeOffset? OpenUntil { get; set; }
    }
}
