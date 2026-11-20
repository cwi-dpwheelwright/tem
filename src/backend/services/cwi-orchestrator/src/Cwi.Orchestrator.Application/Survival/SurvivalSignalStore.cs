using System.Collections.Concurrent;

namespace Cwi.Orchestrator.Application.Survival;

public sealed class SurvivalSignalStore
{
    private readonly ConcurrentQueue<SurvivalSignalDto> _signals = new();
    private const int MaxSignals = 1000;

    public void Record(SurvivalSignalDto signal)
    {
        _signals.Enqueue(signal);
        while (_signals.Count > MaxSignals && _signals.TryDequeue(out _))
        {
        }
    }

    public SurvivalSignalDto? GetLatest()
    {
        return _signals.LastOrDefault();
    }
}
