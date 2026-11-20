using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Domain.Repositories;

public interface IArtifactRegistryRepository
{
    Task<ArtifactRecord?> GetAsync(string tenantId, string artifactId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ArtifactRecord>> ListAsync(string tenantId, string? artifactType, CancellationToken cancellationToken);
    Task UpsertAsync(ArtifactRecord record, CancellationToken cancellationToken);
}
