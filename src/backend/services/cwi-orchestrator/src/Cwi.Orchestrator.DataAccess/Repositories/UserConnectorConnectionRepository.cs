using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Cwi.Orchestrator.DataAccess.Repositories;

public sealed class UserConnectorConnectionRepository : IUserConnectorConnectionRepository
{
    private readonly OrchestratorDbContext _db;

    public UserConnectorConnectionRepository(OrchestratorDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<UserConnectorConnection>> ListForUserAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        return await _db.UserConnectorConnections
            .Where(connection => connection.TenantId == tenantId && connection.UserId == userId)
            .OrderBy(connection => connection.ConnectorType)
            .ThenBy(connection => connection.DisplayName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<UserConnectorConnection?> GetForUserAsync(
        string tenantId,
        string userId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        return _db.UserConnectorConnections.FirstOrDefaultAsync(
            connection => connection.TenantId == tenantId
                && connection.UserId == userId
                && connection.ConnectionId == connectionId,
            cancellationToken);
    }

    public async Task UpsertAsync(UserConnectorConnection connection, CancellationToken cancellationToken)
    {
        var existing = await _db.UserConnectorConnections.FirstOrDefaultAsync(
            item => item.TenantId == connection.TenantId
                && item.UserId == connection.UserId
                && item.ConnectionId == connection.ConnectionId,
            cancellationToken).ConfigureAwait(false);

        if (existing is null)
        {
            _db.UserConnectorConnections.Add(connection);
        }
        else
        {
            existing.ConnectorType = connection.ConnectorType;
            existing.DisplayName = connection.DisplayName;
            existing.Status = connection.Status;
            existing.AuthType = connection.AuthType;
            existing.CredentialRef = connection.CredentialRef;
            existing.ScopesJson = connection.ScopesJson;
            existing.ConfigJson = connection.ConfigJson;
            existing.SyncEnabled = connection.SyncEnabled;
            existing.SyncFrequencyMinutes = connection.SyncFrequencyMinutes;
            existing.VisibilityScope = connection.VisibilityScope;
            existing.LocalOnly = connection.LocalOnly;
            existing.LastSyncStatus = connection.LastSyncStatus;
            existing.LastSyncUtc = connection.LastSyncUtc;
            existing.LastError = connection.LastError;
            existing.ProvenanceId = connection.ProvenanceId;
            existing.UpdatedUtc = connection.UpdatedUtc;
            existing.DisconnectedUtc = connection.DisconnectedUtc;
        }

        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
