namespace Cwi.Router.Application.Survival;

public sealed class SurvivalStateStore
{
    private readonly object _lock = new();
    private SurvivalState _state = new();

    public SurvivalState GetSnapshot()
    {
        lock (_lock)
        {
            return _state;
        }
    }

    public void Update(SurvivalState state)
    {
        lock (_lock)
        {
            _state = state;
        }
    }

    public void Update(string mode, string source, string? profile = null, DateTimeOffset? lastSignalUtc = null)
    {
        Update(new SurvivalState
        {
            Mode = mode,
            Profile = profile,
            LastSignalUtc = lastSignalUtc,
            UpdatedUtc = DateTimeOffset.UtcNow,
            Source = source
        });
    }
}
