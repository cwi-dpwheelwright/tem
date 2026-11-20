using Cwi.Coordination.Domain.Entities;

namespace Cwi.Coordination.Domain.Repositories;

public interface ISurvivalModeRepository
{
    Task<SurvivalModeState> GetAsync(CancellationToken cancellationToken);
    Task<SurvivalModeState> SetAsync(SurvivalModeState state, CancellationToken cancellationToken);
}
