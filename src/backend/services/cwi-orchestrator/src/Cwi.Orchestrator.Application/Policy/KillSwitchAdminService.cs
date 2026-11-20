using System.Text.Json;
using System.Text.Json.Nodes;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Policy;

public sealed class KillSwitchAdminService : DomainServiceBase
{
    private readonly ITenantConfigRepository _tenantConfigRepository;
    private readonly KillSwitchAdminOptions _options;
    private readonly KillSwitchTokenStore _tokenStore;

    public KillSwitchAdminService(
        ITenantConfigRepository tenantConfigRepository,
        KillSwitchAdminOptions options,
        KillSwitchTokenStore tokenStore)
    {
        _tenantConfigRepository = tenantConfigRepository;
        _options = options;
        _tokenStore = tokenStore;
    }

    public Task<ServiceResponse<KillSwitchStatus>> GetAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Task.FromResult(ServiceResponse<KillSwitchStatus>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id required"),
                statusCode: 400,
                message: "tenant_id required"));
        }

        return ExecuteAsync(async () =>
        {
            var config = await _tenantConfigRepository.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var (enabled, irreversible, updatedUtc) = ReadState(config?.ConfigJson);
            return new KillSwitchStatus
            {
                Enabled = enabled,
                Irreversible = irreversible,
                UpdatedUtc = updatedUtc
            };
        });
    }

    public Task<ServiceResponse<KillSwitchStatus>> SetAsync(
        string tenantId,
        bool enabled,
        bool irreversible,
        string? killCode,
        string? tokenId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Task.FromResult(ServiceResponse<KillSwitchStatus>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id required"),
                statusCode: 400,
                message: "tenant_id required"));
        }

        if (!_options.Enabled)
        {
            return Task.FromResult(ServiceResponse<KillSwitchStatus>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Kill switch admin disabled."),
                statusCode: 403,
                message: "Kill switch admin disabled"));
        }

        if (!IsKillCodeValid(killCode))
        {
            return Task.FromResult(ServiceResponse<KillSwitchStatus>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Kill code invalid."),
                statusCode: 403,
                message: "kill code invalid"));
        }

        if (_options.TokenRequired || !string.IsNullOrWhiteSpace(tokenId))
        {
            string? reason = null;
            if (string.IsNullOrWhiteSpace(tokenId) ||
                !_tokenStore.TryConsume(tokenId, tenantId, out reason))
            {
                return Task.FromResult(ServiceResponse<KillSwitchStatus>.Fail(
                    new DomainError(ErrorCodes.Forbidden, $"Kill token invalid ({reason ?? "unknown"})."),
                    statusCode: 403,
                    message: "kill token invalid"));
            }
        }

        return ExecuteAsync(async () =>
        {
            var config = await _tenantConfigRepository.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
            var existingState = ReadState(config?.ConfigJson);
            if (existingState.Irreversible && !enabled)
            {
                return ServiceResponse<KillSwitchStatus>.Fail(
                    new DomainError(ErrorCodes.Forbidden, "Kill switch irreversible."),
                    statusCode: 409,
                    message: "kill switch irreversible");
            }

            var now = DateTimeOffset.UtcNow;
            var nextState = new KillSwitchStatus
            {
                Enabled = enabled,
                Irreversible = existingState.Irreversible || irreversible,
                UpdatedUtc = now
            };

            var updated = config ?? new TenantConfig
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId.Trim(),
                CreatedUtc = now
            };

            updated.UpdatedUtc = now;
            updated.ConfigJson = UpdateConfigJson(updated.ConfigJson, nextState);

            await _tenantConfigRepository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
            return ServiceResponse<KillSwitchStatus>.Success(nextState);
        });
    }

    public Task<ServiceResponse<KillSwitchToken>> IssueTokenAsync(
        string tenantId,
        string? killCode,
        int? ttlSeconds,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return Task.FromResult(ServiceResponse<KillSwitchToken>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id required"),
                statusCode: 400,
                message: "tenant_id required"));
        }

        if (!_options.Enabled)
        {
            return Task.FromResult(ServiceResponse<KillSwitchToken>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Kill switch admin disabled."),
                statusCode: 403,
                message: "Kill switch admin disabled"));
        }

        if (!IsKillCodeValid(killCode))
        {
            return Task.FromResult(ServiceResponse<KillSwitchToken>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Kill code invalid."),
                statusCode: 403,
                message: "kill code invalid"));
        }

        var ttl = ResolveTokenTtl(ttlSeconds);
        var token = _tokenStore.Issue(tenantId, ttl);
        return Task.FromResult(ServiceResponse<KillSwitchToken>.Success(token));
    }

    private bool IsKillCodeValid(string? killCode)
    {
        if (string.IsNullOrWhiteSpace(_options.KillCode))
        {
            return true;
        }

        return string.Equals(_options.KillCode, killCode?.Trim(), StringComparison.Ordinal);
    }

    private TimeSpan ResolveTokenTtl(int? requestedSeconds)
    {
        var ttlSeconds = _options.TokenTtlSeconds;
        if (requestedSeconds.HasValue && requestedSeconds.Value > 0)
        {
            ttlSeconds = Math.Min(requestedSeconds.Value, _options.TokenMaxTtlSeconds);
        }

        if (ttlSeconds <= 0)
        {
            ttlSeconds = 60;
        }

        return TimeSpan.FromSeconds(ttlSeconds);
    }

    private static string UpdateConfigJson(string? existingJson, KillSwitchStatus nextState)
    {
        JsonNode rootNode;
        if (string.IsNullOrWhiteSpace(existingJson))
        {
            rootNode = new JsonObject();
        }
        else
        {
            try
            {
                rootNode = JsonNode.Parse(existingJson) ?? new JsonObject();
            }
            catch (JsonException)
            {
                rootNode = new JsonObject();
            }
        }

        if (rootNode is not JsonObject rootObject)
        {
            rootObject = new JsonObject();
        }

        var killSwitch = rootObject["kill_switch"] as JsonObject ?? new JsonObject();
        killSwitch["enabled"] = nextState.Enabled;
        killSwitch["irreversible"] = nextState.Irreversible;
        killSwitch["state"] = nextState.Irreversible ? "irreversible" : nextState.Enabled ? "enabled" : "disabled";
        killSwitch["updated_utc"] = nextState.UpdatedUtc.ToString("O");

        rootObject["kill_switch"] = killSwitch;

        return rootObject.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = false
        });
    }

    private static (bool Enabled, bool Irreversible, DateTimeOffset UpdatedUtc) ReadState(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
        {
            return (false, false, DateTimeOffset.MinValue);
        }

        try
        {
            using var document = JsonDocument.Parse(configJson);
            var root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("kill_switch", out var killSwitch))
            {
                var enabled = GetBool(killSwitch, "enabled");
                var irreversible = GetBool(killSwitch, "irreversible");
                var updatedUtc = GetDateTime(killSwitch, "updated_utc");
                return (enabled, irreversible, updatedUtc);
            }

            if (root.ValueKind == JsonValueKind.True || root.ValueKind == JsonValueKind.False)
            {
                return (root.ValueKind == JsonValueKind.True, false, DateTimeOffset.MinValue);
            }
        }
        catch (JsonException)
        {
            return (false, false, DateTimeOffset.MinValue);
        }

        return (false, false, DateTimeOffset.MinValue);
    }

    private static bool GetBool(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return false;
    }

    private static DateTimeOffset GetDateTime(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value))
        {
            return DateTimeOffset.MinValue;
        }

        if (value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return DateTimeOffset.MinValue;
    }
}

public sealed class KillSwitchStatus
{
    public bool Enabled { get; init; }
    public bool Irreversible { get; init; }
    public DateTimeOffset UpdatedUtc { get; init; }
}
