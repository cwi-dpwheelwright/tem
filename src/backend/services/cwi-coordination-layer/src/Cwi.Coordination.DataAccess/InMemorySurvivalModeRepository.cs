using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.DataAccess;

public sealed class InMemorySurvivalModeRepository : ISurvivalModeRepository
{
    private SurvivalModeState _state = new()
    {
        Enabled = false,
        Reason = null
    };

    public Task<SurvivalModeState> GetAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_state);
    }

    public Task<SurvivalModeState> SetAsync(SurvivalModeState state, CancellationToken cancellationToken)
    {
        _state = state;
        return Task.FromResult(state);
    }
}
