#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class ConnectorCatalogItemDto
{
    [JsonPropertyName("connector_type")]
    public string ConnectorType { get; init; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("auth_types")]
    public IReadOnlyList<string> AuthTypes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("default_scopes")]
    public IReadOnlyList<string> DefaultScopes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("configurable_fields")]
    public IReadOnlyList<string> ConfigurableFields { get; init; } = Array.Empty<string>();

    [JsonPropertyName("supports_sync")]
    public bool SupportsSync { get; init; } = true;

    [JsonPropertyName("authorization_configured")]
    public bool AuthorizationConfigured { get; init; }
}

public sealed class AccountConnectorConnectionDto
{
    [JsonPropertyName("connection_id")]
    public string ConnectionId { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("connector_type")]
    public string ConnectorType { get; init; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("auth_type")]
    public string AuthType { get; init; } = string.Empty;

    [JsonPropertyName("credential_configured")]
    public bool CredentialConfigured { get; init; }

    [JsonPropertyName("authorization_url")]
    public string? AuthorizationUrl { get; init; }

    [JsonPropertyName("authorization_configured")]
    public bool AuthorizationConfigured { get; init; }

    [JsonPropertyName("credential_ref")]
    public string? CredentialRef { get; init; }

    [JsonPropertyName("scopes")]
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    [JsonPropertyName("config")]
    public IReadOnlyDictionary<string, object?> Config { get; init; } = new Dictionary<string, object?>();

    [JsonPropertyName("sync_enabled")]
    public bool SyncEnabled { get; init; }

    [JsonPropertyName("sync_frequency_minutes")]
    public int SyncFrequencyMinutes { get; init; }

    [JsonPropertyName("visibility_scope")]
    public string VisibilityScope { get; init; } = "private";

    [JsonPropertyName("local_only")]
    public bool LocalOnly { get; init; }

    [JsonPropertyName("last_sync_status")]
    public string? LastSyncStatus { get; init; }

    [JsonPropertyName("last_sync_utc")]
    public DateTimeOffset? LastSyncUtc { get; init; }

    [JsonPropertyName("last_error")]
    public string? LastError { get; init; }

    [JsonPropertyName("provenance_id")]
    public string? ProvenanceId { get; init; }

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("updated_utc")]
    public DateTimeOffset UpdatedUtc { get; init; }

    [JsonPropertyName("disconnected_utc")]
    public DateTimeOffset? DisconnectedUtc { get; init; }
}

public sealed class AccountConnectorListDto
{
    [JsonPropertyName("available")]
    public IReadOnlyList<ConnectorCatalogItemDto> Available { get; init; } = Array.Empty<ConnectorCatalogItemDto>();

    [JsonPropertyName("connections")]
    public IReadOnlyList<AccountConnectorConnectionDto> Connections { get; init; } = Array.Empty<AccountConnectorConnectionDto>();
}

public sealed class AccountConnectorCreateRequestDto
{
    [JsonPropertyName("connector_type")]
    public string ConnectorType { get; init; } = string.Empty;

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("auth_type")]
    public string? AuthType { get; init; }

    [JsonPropertyName("credential_reference")]
    public string? CredentialReference { get; init; }

    [JsonPropertyName("scopes")]
    public IReadOnlyList<string>? Scopes { get; init; }

    [JsonPropertyName("config")]
    public Dictionary<string, object?>? Config { get; init; }

    [JsonPropertyName("sync_enabled")]
    public bool? SyncEnabled { get; init; }

    [JsonPropertyName("sync_frequency_minutes")]
    public int? SyncFrequencyMinutes { get; init; }

    [JsonPropertyName("visibility_scope")]
    public string? VisibilityScope { get; init; }

    [JsonPropertyName("local_only")]
    public bool? LocalOnly { get; init; }
}

public sealed class AccountConnectorUpdateRequestDto
{
    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("credential_reference")]
    public string? CredentialReference { get; init; }

    [JsonPropertyName("scopes")]
    public IReadOnlyList<string>? Scopes { get; init; }

    [JsonPropertyName("config")]
    public Dictionary<string, object?>? Config { get; init; }

    [JsonPropertyName("sync_enabled")]
    public bool? SyncEnabled { get; init; }

    [JsonPropertyName("sync_frequency_minutes")]
    public int? SyncFrequencyMinutes { get; init; }

    [JsonPropertyName("visibility_scope")]
    public string? VisibilityScope { get; init; }

    [JsonPropertyName("local_only")]
    public bool? LocalOnly { get; init; }
}

public sealed class AccountConnectorSyncRequestDto
{
    [JsonPropertyName("force")]
    public bool Force { get; init; }
}
#nullable disable
