#nullable enable
using System.Text;
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Connectors;

public sealed class AccountConnectorService : DomainServiceBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlyDictionary<string, ConnectorCatalogDefinition> Catalog =
        new[]
        {
            new ConnectorCatalogDefinition(
                "google_drive",
                "Google Drive",
                "Sign in to Google Drive to connect files and folders.",
                new[] { "oauth2" },
                new[] { "drive.readonly" },
                Array.Empty<string>(),
                "google",
                new[] { "https://www.googleapis.com/auth/drive.readonly" }),
            new ConnectorCatalogDefinition(
                "onedrive",
                "OneDrive",
                "Sign in to OneDrive to connect files and folders.",
                new[] { "oauth2" },
                new[] { "files.read" },
                Array.Empty<string>(),
                "microsoft",
                new[] { "offline_access", "Files.Read", "User.Read" }),
            new ConnectorCatalogDefinition(
                "outlook",
                "Outlook",
                "Sign in to Outlook to connect mail and calendar context.",
                new[] { "oauth2" },
                new[] { "mail.read" },
                Array.Empty<string>(),
                "microsoft",
                new[] { "offline_access", "Mail.Read", "Calendars.Read", "User.Read" })
        }.ToDictionary(item => item.ConnectorType, StringComparer.OrdinalIgnoreCase);

    private readonly IUserConnectorConnectionRepository _repository;
    private readonly AccountConnectorOAuthOptions _oauthOptions;

    public AccountConnectorService(
        IUserConnectorConnectionRepository repository,
        AccountConnectorOAuthOptions? oauthOptions = null)
    {
        _repository = repository;
        _oauthOptions = oauthOptions ?? AccountConnectorOAuthOptions.Empty;
    }

    public Task<ServiceResponse<AccountConnectorListDto>> ListAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleListAsync(tenantId, userId, cancellationToken));
    }

    public Task<ServiceResponse<AccountConnectorConnectionDto>> CreateAsync(
        string tenantId,
        string userId,
        AccountConnectorCreateRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleCreateAsync(tenantId, userId, request, cancellationToken));
    }

    public Task<ServiceResponse<AccountConnectorConnectionDto>> UpdateAsync(
        string tenantId,
        string userId,
        string connectionId,
        AccountConnectorUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleUpdateAsync(tenantId, userId, connectionId, request, cancellationToken));
    }

    public Task<ServiceResponse<AccountConnectorConnectionDto>> DisconnectAsync(
        string tenantId,
        string userId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleDisconnectAsync(tenantId, userId, connectionId, cancellationToken));
    }

    public Task<ServiceResponse<AccountConnectorConnectionDto>> QueueSyncAsync(
        string tenantId,
        string userId,
        string connectionId,
        AccountConnectorSyncRequestDto request,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(() => HandleQueueSyncAsync(tenantId, userId, connectionId, request, cancellationToken));
    }

    private async Task<ServiceResponse<AccountConnectorListDto>> HandleListAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        var identity = NormalizeIdentity(tenantId, userId);
        if (identity.Error is not null)
        {
            return FailList(identity.Error);
        }

        var connections = await _repository
            .ListForUserAsync(identity.TenantId, identity.UserId, cancellationToken)
            .ConfigureAwait(false);
        return ServiceResponse<AccountConnectorListDto>.Success(new AccountConnectorListDto
        {
            Available = Catalog.Values.Select(MapCatalog).ToList(),
            Connections = connections
                .Where(connection => !string.Equals(connection.Status, "disconnected", StringComparison.OrdinalIgnoreCase))
                .Select(connection => MapConnection(connection, ResolveCatalog(connection.ConnectorType)))
                .ToList()
        });
    }

    private async Task<ServiceResponse<AccountConnectorConnectionDto>> HandleCreateAsync(
        string tenantId,
        string userId,
        AccountConnectorCreateRequestDto request,
        CancellationToken cancellationToken)
    {
        var identity = NormalizeIdentity(tenantId, userId);
        if (identity.Error is not null)
        {
            return FailConnection(identity.Error);
        }

        if (!TryGetCatalog(request.ConnectorType, out var catalog))
        {
            return FailConnection("Unknown connector type.", 400);
        }

        var authType = NormalizeAuthType(request.AuthType, catalog);
        if (authType is null)
        {
            return FailConnection("Unsupported connector auth type.", 400);
        }

        var credential = NormalizeCredentialReference(request.CredentialReference);
        if (credential.Error is not null)
        {
            return FailConnection(credential.Error);
        }

        var config = NormalizeConfig(request.Config);
        if (config.Error is not null)
        {
            return FailConnection(config.Error);
        }

        var visibility = NormalizeVisibility(request.VisibilityScope);
        if (visibility is null)
        {
            return FailConnection("Visibility scope must be private, tenant, or home_node.", 400);
        }

        var localOnly = request.LocalOnly ?? string.Equals(visibility, "home_node", StringComparison.OrdinalIgnoreCase);
        if (localOnly && !string.Equals(visibility, "home_node", StringComparison.OrdinalIgnoreCase))
        {
            return FailConnection("local_only requires home_node visibility.", 400);
        }

        var now = DateTimeOffset.UtcNow;
        var record = new UserConnectorConnection
        {
            Id = Guid.NewGuid(),
            ConnectionId = $"conn_{Guid.NewGuid():N}",
            TenantId = identity.TenantId,
            UserId = identity.UserId,
            ConnectorType = catalog.ConnectorType,
            DisplayName = NormalizeDisplayName(request.DisplayName, catalog.DisplayName),
            Status = string.IsNullOrWhiteSpace(credential.Value) ? "pending_auth" : "connected",
            AuthType = authType,
            CredentialRef = credential.Value,
            ScopesJson = SerializeStringList(NormalizeScopes(request.Scopes, catalog.DefaultScopes)),
            ConfigJson = config.Json,
            SyncEnabled = request.SyncEnabled ?? false,
            SyncFrequencyMinutes = NormalizeSyncFrequency(request.SyncFrequencyMinutes),
            VisibilityScope = visibility,
            LocalOnly = localOnly,
            LastSyncStatus = "not_started",
            ProvenanceId = $"prov_connector_{Guid.NewGuid():N}",
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _repository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        return ServiceResponse<AccountConnectorConnectionDto>.Success(MapConnection(record, catalog), statusCode: 201);
    }

    private async Task<ServiceResponse<AccountConnectorConnectionDto>> HandleUpdateAsync(
        string tenantId,
        string userId,
        string connectionId,
        AccountConnectorUpdateRequestDto request,
        CancellationToken cancellationToken)
    {
        var existingResult = await LoadConnectionAsync(tenantId, userId, connectionId, cancellationToken)
            .ConfigureAwait(false);
        if (!existingResult.IsSuccess || existingResult.Data is null)
        {
            return MapLoadFailure(existingResult);
        }

        var record = existingResult.Data;
        if (!TryGetCatalog(record.ConnectorType, out var catalog))
        {
            return FailConnection("Unknown connector type.", 400);
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            record.DisplayName = NormalizeDisplayName(request.DisplayName, catalog.DisplayName);
        }

        if (request.CredentialReference is not null)
        {
            var credential = NormalizeCredentialReference(request.CredentialReference);
            if (credential.Error is not null)
            {
                return FailConnection(credential.Error);
            }

            record.CredentialRef = credential.Value;
            if (string.IsNullOrWhiteSpace(record.CredentialRef))
            {
                record.Status = "pending_auth";
                record.SyncEnabled = false;
            }
            else if (string.Equals(record.Status, "pending_auth", StringComparison.OrdinalIgnoreCase))
            {
                record.Status = "connected";
            }
        }

        if (request.Scopes is not null)
        {
            record.ScopesJson = SerializeStringList(NormalizeScopes(request.Scopes, catalog.DefaultScopes));
        }

        if (request.Config is not null)
        {
            var config = NormalizeConfig(request.Config);
            if (config.Error is not null)
            {
                return FailConnection(config.Error);
            }

            record.ConfigJson = config.Json;
        }

        if (request.VisibilityScope is not null)
        {
            var visibility = NormalizeVisibility(request.VisibilityScope);
            if (visibility is null)
            {
                return FailConnection("Visibility scope must be private, tenant, or home_node.", 400);
            }

            record.VisibilityScope = visibility;
            if (!string.Equals(visibility, "home_node", StringComparison.OrdinalIgnoreCase))
            {
                record.LocalOnly = false;
            }
        }

        if (request.LocalOnly.HasValue)
        {
            if (request.LocalOnly.Value && !string.Equals(record.VisibilityScope, "home_node", StringComparison.OrdinalIgnoreCase))
            {
                return FailConnection("local_only requires home_node visibility.", 400);
            }

            record.LocalOnly = request.LocalOnly.Value;
        }

        if (request.SyncEnabled.HasValue)
        {
            record.SyncEnabled = request.SyncEnabled.Value;
        }

        if (request.SyncFrequencyMinutes.HasValue)
        {
            record.SyncFrequencyMinutes = NormalizeSyncFrequency(request.SyncFrequencyMinutes);
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var normalizedStatus = NormalizeMutableStatus(request.Status);
            if (normalizedStatus is null)
            {
                return FailConnection("Status must be connected, pending_auth, or paused.", 400);
            }

            if (string.Equals(normalizedStatus, "connected", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(record.CredentialRef))
            {
                return FailConnection("A credential reference is required before this connector can be connected.", 409);
            }

            record.Status = normalizedStatus;
        }

        record.UpdatedUtc = DateTimeOffset.UtcNow;
        await _repository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        return ServiceResponse<AccountConnectorConnectionDto>.Success(MapConnection(record, catalog));
    }

    private async Task<ServiceResponse<AccountConnectorConnectionDto>> HandleDisconnectAsync(
        string tenantId,
        string userId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var existingResult = await LoadConnectionAsync(tenantId, userId, connectionId, cancellationToken)
            .ConfigureAwait(false);
        if (!existingResult.IsSuccess || existingResult.Data is null)
        {
            return MapLoadFailure(existingResult);
        }

        var record = existingResult.Data;
        var now = DateTimeOffset.UtcNow;
        record.Status = "disconnected";
        record.SyncEnabled = false;
        record.LastSyncStatus = "disconnected";
        record.UpdatedUtc = now;
        record.DisconnectedUtc = now;
        await _repository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        return ServiceResponse<AccountConnectorConnectionDto>.Success(MapConnection(record, ResolveCatalog(record.ConnectorType)));
    }

    private async Task<ServiceResponse<AccountConnectorConnectionDto>> HandleQueueSyncAsync(
        string tenantId,
        string userId,
        string connectionId,
        AccountConnectorSyncRequestDto request,
        CancellationToken cancellationToken)
    {
        var existingResult = await LoadConnectionAsync(tenantId, userId, connectionId, cancellationToken)
            .ConfigureAwait(false);
        if (!existingResult.IsSuccess || existingResult.Data is null)
        {
            return MapLoadFailure(existingResult);
        }

        var record = existingResult.Data;
        if (!string.Equals(record.Status, "connected", StringComparison.OrdinalIgnoreCase))
        {
            return FailConnection("Connector authorization is required before sync can run.", 409);
        }

        if (!record.SyncEnabled && !request.Force)
        {
            return FailConnection("Connector sync is disabled.", 409);
        }

        record.LastSyncStatus = "queued";
        record.LastSyncUtc = DateTimeOffset.UtcNow;
        record.LastError = null;
        record.UpdatedUtc = record.LastSyncUtc.Value;
        await _repository.UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        return ServiceResponse<AccountConnectorConnectionDto>.Success(MapConnection(record, ResolveCatalog(record.ConnectorType)));
    }

    private async Task<ServiceResponse<UserConnectorConnection>> LoadConnectionAsync(
        string tenantId,
        string userId,
        string connectionId,
        CancellationToken cancellationToken)
    {
        var identity = NormalizeIdentity(tenantId, userId);
        if (identity.Error is not null)
        {
            return FailRecord(identity.Error);
        }

        var normalizedConnectionId = connectionId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedConnectionId))
        {
            return FailRecord("connection_id is required.", 400);
        }

        var record = await _repository.GetForUserAsync(
            identity.TenantId,
            identity.UserId,
            normalizedConnectionId,
            cancellationToken).ConfigureAwait(false);
        if (record is null || string.Equals(record.Status, "disconnected", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResponse<UserConnectorConnection>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "Connector connection not found."),
                statusCode: 404,
                message: "connector not found");
        }

        return ServiceResponse<UserConnectorConnection>.Success(record);
    }

    private ConnectorCatalogItemDto MapCatalog(ConnectorCatalogDefinition item)
    {
        return new ConnectorCatalogItemDto
        {
            ConnectorType = item.ConnectorType,
            DisplayName = item.DisplayName,
            Description = item.Description,
            AuthTypes = item.AuthTypes,
            DefaultScopes = item.DefaultScopes,
            ConfigurableFields = item.ConfigurableFields,
            SupportsSync = true,
            AuthorizationConfigured = IsAuthorizationConfigured(item)
        };
    }

    private bool IsAuthorizationConfigured(ConnectorCatalogDefinition item)
    {
        var redirectUri = ResolveRedirectUri(item.OAuthProvider);
        var clientId = item.OAuthProvider switch
        {
            "google" => _oauthOptions.GoogleClientId,
            "microsoft" => _oauthOptions.MicrosoftClientId,
            _ => null
        };
        return !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(redirectUri);
    }

    private AccountConnectorConnectionDto MapConnection(UserConnectorConnection record, ConnectorCatalogDefinition? catalog)
    {
        var authorization = BuildAuthorization(record, catalog);
        return new AccountConnectorConnectionDto
        {
            ConnectionId = record.ConnectionId,
            TenantId = record.TenantId,
            UserId = record.UserId,
            ConnectorType = record.ConnectorType,
            DisplayName = record.DisplayName,
            Status = record.Status,
            AuthType = record.AuthType,
            CredentialConfigured = !string.IsNullOrWhiteSpace(record.CredentialRef),
            AuthorizationUrl = authorization.Url,
            AuthorizationConfigured = authorization.Configured,
            CredentialRef = record.CredentialRef,
            Scopes = DeserializeStringList(record.ScopesJson),
            Config = DeserializeDictionary(record.ConfigJson),
            SyncEnabled = record.SyncEnabled,
            SyncFrequencyMinutes = record.SyncFrequencyMinutes,
            VisibilityScope = record.VisibilityScope,
            LocalOnly = record.LocalOnly,
            LastSyncStatus = record.LastSyncStatus,
            LastSyncUtc = record.LastSyncUtc,
            LastError = record.LastError,
            ProvenanceId = record.ProvenanceId,
            CreatedUtc = record.CreatedUtc,
            UpdatedUtc = record.UpdatedUtc,
            DisconnectedUtc = record.DisconnectedUtc
        };
    }

    private ConnectorAuthorization BuildAuthorization(UserConnectorConnection record, ConnectorCatalogDefinition? catalog)
    {
        if (catalog is null ||
            !string.Equals(record.AuthType, "oauth2", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(record.Status, "disconnected", StringComparison.OrdinalIgnoreCase))
        {
            return new ConnectorAuthorization(null, false);
        }

        var redirectUri = ResolveRedirectUri(catalog.OAuthProvider);
        var state = BuildState(record);

        return catalog.OAuthProvider switch
        {
            "google" => BuildGoogleAuthorizationUrl(_oauthOptions.GoogleClientId, redirectUri, catalog.OAuthScopes, state),
            "microsoft" => BuildMicrosoftAuthorizationUrl(_oauthOptions.MicrosoftClientId, redirectUri, catalog.OAuthScopes, state),
            _ => new ConnectorAuthorization(null, false)
        };
    }

    private ConnectorAuthorization BuildGoogleAuthorizationUrl(
        string? clientId,
        string? redirectUri,
        IReadOnlyList<string> scopes,
        string state)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
        {
            return new ConnectorAuthorization(null, false);
        }

        return new ConnectorAuthorization(BuildUrl(
            "https://accounts.google.com/o/oauth2/v2/auth",
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["scope"] = string.Join(" ", scopes),
                ["access_type"] = "offline",
                ["prompt"] = "consent",
                ["state"] = state
            }), true);
    }

    private ConnectorAuthorization BuildMicrosoftAuthorizationUrl(
        string? clientId,
        string? redirectUri,
        IReadOnlyList<string> scopes,
        string state)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(redirectUri))
        {
            return new ConnectorAuthorization(null, false);
        }

        var tenant = NormalizeMicrosoftTenant(_oauthOptions.MicrosoftTenant);
        return new ConnectorAuthorization(BuildUrl(
            $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize",
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["redirect_uri"] = redirectUri,
                ["response_type"] = "code",
                ["response_mode"] = "query",
                ["scope"] = string.Join(" ", scopes),
                ["state"] = state
            }), true);
    }

    private string? ResolveRedirectUri(string provider)
    {
        var configured = provider switch
        {
            "google" => _oauthOptions.GoogleRedirectUri ?? _oauthOptions.DefaultRedirectUri,
            "microsoft" => _oauthOptions.MicrosoftRedirectUri ?? _oauthOptions.DefaultRedirectUri,
            _ => _oauthOptions.DefaultRedirectUri
        };
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        if (string.IsNullOrWhiteSpace(_oauthOptions.PublicBaseUrl) ||
            !Uri.TryCreate(_oauthOptions.PublicBaseUrl.TrimEnd('/'), UriKind.Absolute, out var baseUri) ||
            baseUri.Scheme is not ("https" or "http"))
        {
            return null;
        }

        var callbackPath = string.IsNullOrWhiteSpace(_oauthOptions.CallbackPath)
            ? "/api/account/connectors/oauth/callback"
            : _oauthOptions.CallbackPath.Trim();
        if (!callbackPath.StartsWith("/", StringComparison.Ordinal))
        {
            callbackPath = "/" + callbackPath;
        }

        return new Uri(baseUri, callbackPath).ToString();
    }

    private static string BuildState(UserConnectorConnection record)
    {
        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["schema_version"] = "cwi.connector.oauth.state.v1",
            ["tenant_id"] = record.TenantId,
            ["user_id"] = record.UserId,
            ["connection_id"] = record.ConnectionId,
            ["connector_type"] = record.ConnectorType,
            ["nonce"] = Guid.NewGuid().ToString("N")
        }, JsonOptions);
        return Base64UrlEncode(payload);
    }

    private static string BuildUrl(string endpoint, IReadOnlyDictionary<string, string> query)
    {
        return endpoint + "?" + string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
    }

    private static string Base64UrlEncode(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string NormalizeMicrosoftTenant(string? tenant)
    {
        var value = string.IsNullOrWhiteSpace(tenant) ? "common" : tenant.Trim();
        return value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '.' or '_')
            ? value
            : "common";
    }

    private static (string TenantId, string UserId, string? Error) NormalizeIdentity(string tenantId, string userId)
    {
        var normalizedTenantId = tenantId.Trim();
        var normalizedUserId = userId.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTenantId))
        {
            return (string.Empty, string.Empty, "tenant_id is required.");
        }

        if (string.IsNullOrWhiteSpace(normalizedUserId))
        {
            return (string.Empty, string.Empty, "user_id is required.");
        }

        return (normalizedTenantId, normalizedUserId, null);
    }

    private static bool TryGetCatalog(string connectorType, out ConnectorCatalogDefinition catalog)
    {
        var key = connectorType.Trim();
        return Catalog.TryGetValue(key, out catalog!);
    }

    private static ConnectorCatalogDefinition? ResolveCatalog(string connectorType)
    {
        return TryGetCatalog(connectorType, out var catalog) ? catalog : null;
    }

    private static string? NormalizeAuthType(string? authType, ConnectorCatalogDefinition catalog)
    {
        var normalized = string.IsNullOrWhiteSpace(authType)
            ? catalog.AuthTypes.FirstOrDefault()
            : authType.Trim().ToLowerInvariant();
        return normalized is not null && catalog.AuthTypes.Contains(normalized, StringComparer.OrdinalIgnoreCase)
            ? normalized
            : null;
    }

    private static string NormalizeDisplayName(string? displayName, string fallback)
    {
        var value = displayName?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Length <= 120 ? value : value[..120];
    }

    private static IReadOnlyList<string> NormalizeScopes(
        IReadOnlyList<string>? scopes,
        IReadOnlyList<string> fallback)
    {
        var source = scopes is { Count: > 0 } ? scopes : fallback;
        return source
            .Select(scope => scope.Trim())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32)
            .ToList();
    }

    private static string? NormalizeVisibility(string? visibilityScope)
    {
        var normalized = string.IsNullOrWhiteSpace(visibilityScope)
            ? "private"
            : visibilityScope.Trim().ToLowerInvariant();
        return normalized is "private" or "tenant" or "home_node" ? normalized : null;
    }

    private static string? NormalizeMutableStatus(string status)
    {
        var normalized = status.Trim().ToLowerInvariant();
        return normalized is "connected" or "pending_auth" or "paused" ? normalized : null;
    }

    private static int NormalizeSyncFrequency(int? value)
    {
        return Math.Clamp(value ?? 360, 15, 10080);
    }

    private static (string? Value, string? Error) NormalizeCredentialReference(string? credentialReference)
    {
        var value = credentialReference?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return (null, null);
        }

        if (value.StartsWith("inline://", StringComparison.OrdinalIgnoreCase)
            || !value.Contains("://", StringComparison.Ordinal))
        {
            return (null, "Credential references must point to a server-side OAuth grant or secret reference.");
        }

        return value.Length <= 512
            ? (value, null)
            : (null, "Credential reference is too long.");
    }

    private static (string Json, string? Error) NormalizeConfig(Dictionary<string, object?>? config)
    {
        if (config is null || config.Count == 0)
        {
            return ("{}", null);
        }

        if (ContainsSensitiveKey(config))
        {
            return ("{}", "Connector config cannot include secrets, tokens, passwords, or API keys.");
        }

        return (JsonSerializer.Serialize(config, JsonOptions), null);
    }

    private static bool ContainsSensitiveKey(Dictionary<string, object?> config)
    {
        foreach (var (key, value) in config)
        {
            if (IsSensitiveKey(key))
            {
                return true;
            }

            if (value is Dictionary<string, object?> nested && ContainsSensitiveKey(nested))
            {
                return true;
            }

            if (value is JsonElement element && ContainsSensitiveKey(element))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsSensitiveKey(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (IsSensitiveKey(property.Name) || ContainsSensitiveKey(property.Value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                if (ContainsSensitiveKey(child))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();
        return normalized is "api_key" or "apikey" or "authorization" or "password" or "token" or "secret"
            || normalized.Contains("private_key", StringComparison.Ordinal)
            || normalized.Contains("client_secret", StringComparison.Ordinal)
            || normalized.EndsWith("_token", StringComparison.Ordinal)
            || normalized.EndsWith("_secret", StringComparison.Ordinal)
            || normalized.EndsWith("_password", StringComparison.Ordinal);
    }

    private static string SerializeStringList(IReadOnlyList<string> values)
    {
        return JsonSerializer.Serialize(values, JsonOptions);
    }

    private static IReadOnlyList<string> DeserializeStringList(string json)
    {
        try
        {
            var values = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
            if (values is not null)
            {
                return values;
            }

            return Array.Empty<string>();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyDictionary<string, object?> DeserializeDictionary(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json, JsonOptions)
                ?? new Dictionary<string, object?>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, object?>();
        }
    }

    private static ServiceResponse<AccountConnectorListDto> FailList(string message, int statusCode = 400)
    {
        return ServiceResponse<AccountConnectorListDto>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, message),
            statusCode,
            message);
    }

    private static ServiceResponse<AccountConnectorConnectionDto> FailConnection(string message, int statusCode = 400)
    {
        return ServiceResponse<AccountConnectorConnectionDto>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, message),
            statusCode,
            message);
    }

    private static ServiceResponse<AccountConnectorConnectionDto> MapLoadFailure(
        ServiceResponse<UserConnectorConnection> result)
    {
        return ServiceResponse<AccountConnectorConnectionDto>.Fail(
            result.Error ?? new DomainError(ErrorCodes.InvalidRequest, result.Message),
            result.StatusCode,
            result.Message);
    }

    private static ServiceResponse<UserConnectorConnection> FailRecord(string message, int statusCode = 400)
    {
        return ServiceResponse<UserConnectorConnection>.Fail(
            new DomainError(ErrorCodes.InvalidRequest, message),
            statusCode,
            message);
    }

    private sealed record ConnectorCatalogDefinition(
        string ConnectorType,
        string DisplayName,
        string Description,
        IReadOnlyList<string> AuthTypes,
        IReadOnlyList<string> DefaultScopes,
        IReadOnlyList<string> ConfigurableFields,
        string OAuthProvider,
        IReadOnlyList<string> OAuthScopes);

    private sealed record ConnectorAuthorization(string? Url, bool Configured);
}
#nullable disable
