using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface ITelemetryWindowRepository
{
    Task AddAsync(TelemetryWindow window, CancellationToken cancellationToken);
}
