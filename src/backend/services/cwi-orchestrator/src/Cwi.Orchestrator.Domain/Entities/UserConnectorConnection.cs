namespace Cwi.Orchestrator.Domain.Entities;

public sealed class UserConnectorConnection
{
    public Guid Id { get; set; }
    public string ConnectionId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ConnectorType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string AuthType { get; set; } = string.Empty;
    public string? CredentialRef { get; set; }
    public string ScopesJson { get; set; } = "[]";
    public string ConfigJson { get; set; } = "{}";
    public bool SyncEnabled { get; set; }
    public int SyncFrequencyMinutes { get; set; }
    public string VisibilityScope { get; set; } = "private";
    public bool LocalOnly { get; set; }
    public string? LastSyncStatus { get; set; }
    public DateTimeOffset? LastSyncUtc { get; set; }
    public string? LastError { get; set; }
    public string? ProvenanceId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; }
    public DateTimeOffset? DisconnectedUtc { get; set; }
}
