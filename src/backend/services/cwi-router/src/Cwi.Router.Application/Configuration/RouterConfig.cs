namespace Cwi.Router.Application.Configuration;

public sealed class RouterConfig
{
    public int HttpPort { get; }
    public string OrchestratorUrl { get; }
    public IReadOnlyList<string> OrchestratorUrls { get; }
    public string? CoordinationLayerUrl { get; }
    public IReadOnlyList<string> CoordinationLayerUrls { get; }
    public string? JwksUrl { get; }
    public string? JwtIssuer { get; }
    public string? JwtAudience { get; }
    public string? IdentityUrl { get; }
    public int TokenEpochCacheSeconds { get; }
    public bool AuthRequired { get; }
    public bool MtlsRequired { get; }
    public string? RedisUrl { get; }
    public int RateLimit { get; }
    public int RateWindowSeconds { get; }
    public string? ModelProUrl { get; }
    public string? ModelFreeUrl { get; }
    public IReadOnlyList<string> ModelProEndpoints { get; }
    public IReadOnlyList<string> ModelFreeEndpoints { get; }
    public IReadOnlyList<string> ModelProCapabilitiesEndpoints { get; }
    public IReadOnlyList<string> ModelFreeCapabilitiesEndpoints { get; }
    public string ModelHealthPath { get; }
    public int ModelHealthProbeIntervalSeconds { get; }
    public int ModelCircuitBreakerFailureThreshold { get; }
    public int ModelCircuitBreakerCooldownSeconds { get; }
    public string PolicyModeDefault { get; }
    public string? DefaultTenantId { get; }
    public int OrchestratorTimeoutSeconds { get; }
    public string? OrchestratorClientCertPath { get; }
    public string? OrchestratorClientCertPassword { get; }
    public string? OrchestratorCaPath { get; }

    private RouterConfig(
        int httpPort,
        string orchestratorUrl,
        IReadOnlyList<string> orchestratorUrls,
        string? coordinationLayerUrl,
        IReadOnlyList<string> coordinationLayerUrls,
        string? jwksUrl,
        string? jwtIssuer,
        string? jwtAudience,
        string? identityUrl,
        int tokenEpochCacheSeconds,
        bool authRequired,
        bool mtlsRequired,
        string? redisUrl,
        int rateLimit,
        int rateWindowSeconds,
        IReadOnlyList<string> modelProEndpoints,
        IReadOnlyList<string> modelFreeEndpoints,
        IReadOnlyList<string> modelProCapabilitiesEndpoints,
        IReadOnlyList<string> modelFreeCapabilitiesEndpoints,
        string modelHealthPath,
        int modelHealthProbeIntervalSeconds,
        int modelCircuitBreakerFailureThreshold,
        int modelCircuitBreakerCooldownSeconds,
        string policyModeDefault,
        string? defaultTenantId,
        int orchestratorTimeoutSeconds,
        string? orchestratorClientCertPath,
        string? orchestratorClientCertPassword,
        string? orchestratorCaPath)
    {
        HttpPort = httpPort;
        OrchestratorUrl = orchestratorUrl;
        OrchestratorUrls = orchestratorUrls;
        CoordinationLayerUrl = coordinationLayerUrl;
        CoordinationLayerUrls = coordinationLayerUrls;
        JwksUrl = jwksUrl;
        JwtIssuer = jwtIssuer;
        JwtAudience = jwtAudience;
        IdentityUrl = identityUrl;
        TokenEpochCacheSeconds = tokenEpochCacheSeconds;
        AuthRequired = authRequired;
        MtlsRequired = mtlsRequired;
        RedisUrl = redisUrl;
        RateLimit = rateLimit;
        RateWindowSeconds = rateWindowSeconds;
        ModelProEndpoints = modelProEndpoints;
        ModelFreeEndpoints = modelFreeEndpoints;
        ModelProCapabilitiesEndpoints = modelProCapabilitiesEndpoints;
        ModelFreeCapabilitiesEndpoints = modelFreeCapabilitiesEndpoints;
        ModelProUrl = modelProEndpoints.FirstOrDefault();
        ModelFreeUrl = modelFreeEndpoints.FirstOrDefault();
        ModelHealthPath = modelHealthPath;
        ModelHealthProbeIntervalSeconds = modelHealthProbeIntervalSeconds;
        ModelCircuitBreakerFailureThreshold = modelCircuitBreakerFailureThreshold;
        ModelCircuitBreakerCooldownSeconds = modelCircuitBreakerCooldownSeconds;
        PolicyModeDefault = policyModeDefault;
        DefaultTenantId = defaultTenantId;
        OrchestratorTimeoutSeconds = orchestratorTimeoutSeconds;
        OrchestratorClientCertPath = orchestratorClientCertPath;
        OrchestratorClientCertPassword = orchestratorClientCertPassword;
        OrchestratorCaPath = orchestratorCaPath;
    }

    public static RouterConfig LoadFromEnvironment()
    {
        var portRaw = Environment.GetEnvironmentVariable("ROUTER_HTTP_PORT") ?? "8081";
        if (!int.TryParse(portRaw, out var httpPort))
        {
            throw new InvalidOperationException("ROUTER_HTTP_PORT must be an integer.");
        }

        var orchestratorUrl = RequireEnv("ORCHESTRATOR_URL");
        var orchestratorUrls = ParseEndpointList(Environment.GetEnvironmentVariable("ORCHESTRATOR_URLS"));
        if (orchestratorUrls.Count == 0)
        {
            orchestratorUrls = new[] { orchestratorUrl };
        }
        var coordinationLayerUrl = Environment.GetEnvironmentVariable("COORDINATION_LAYER_URL");
        var coordinationLayerUrls = ParseEndpointList(Environment.GetEnvironmentVariable("COORDINATION_LAYER_URLS"));
        if (coordinationLayerUrls.Count == 0 && !string.IsNullOrWhiteSpace(coordinationLayerUrl))
        {
            coordinationLayerUrls = new[] { coordinationLayerUrl };
        }
        var coordinationPrimary = coordinationLayerUrls.FirstOrDefault() ?? coordinationLayerUrl;
        var jwksUrl = Environment.GetEnvironmentVariable("JWKS_URL");
        var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER");
        var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE");
        var identityUrl = Environment.GetEnvironmentVariable("IDENTITY_URL");
        var tokenEpochCacheSeconds = ParseInt("TOKEN_EPOCH_CACHE_SECONDS", defaultValue: 60);
        var authRequired = ParseBool("AUTH_REQUIRED", defaultValue: true);
        var mtlsRequired = ParseBool("MTLS_REQUIRED", defaultValue: false);
        var redisUrl = Environment.GetEnvironmentVariable("REDIS_URL");
        var rateLimit = ParseInt("CWI_RATE_LIMIT", defaultValue: 0);
        var rateWindowSeconds = ParseInt("CWI_RATE_WINDOW_SECONDS", defaultValue: 60);
        var modelProUrls = Environment.GetEnvironmentVariable("MODEL_PRO_URLS") ??
            Environment.GetEnvironmentVariable("MODEL_PRO_URL");
        var modelFreeUrls = Environment.GetEnvironmentVariable("MODEL_FREE_URLS") ??
            Environment.GetEnvironmentVariable("MODEL_FREE_URL");
        var modelProEndpoints = ParseEndpointList(modelProUrls);
        var modelFreeEndpoints = ParseEndpointList(modelFreeUrls);
        var modelProCapabilitiesUrls = Environment.GetEnvironmentVariable("MODEL_PRO_CAPABILITIES_URLS") ??
            Environment.GetEnvironmentVariable("MODEL_PRO_CAPABILITIES_URL");
        var modelFreeCapabilitiesUrls = Environment.GetEnvironmentVariable("MODEL_FREE_CAPABILITIES_URLS") ??
            Environment.GetEnvironmentVariable("MODEL_FREE_CAPABILITIES_URL");
        var modelProCapabilitiesEndpoints = ParseEndpointList(modelProCapabilitiesUrls);
        var modelFreeCapabilitiesEndpoints = ParseEndpointList(modelFreeCapabilitiesUrls);
        var modelHealthPath = Environment.GetEnvironmentVariable("MODEL_HEALTH_PATH") ?? "/api/tags";
        var modelHealthProbeIntervalSeconds = ParseInt("MODEL_HEALTH_PROBE_INTERVAL_SECONDS", defaultValue: 30);
        var modelCircuitBreakerFailureThreshold = ParseInt("MODEL_CB_FAILURE_THRESHOLD", defaultValue: 3);
        var modelCircuitBreakerCooldownSeconds = ParseInt("MODEL_CB_COOLDOWN_SECONDS", defaultValue: 30);
        var policyModeDefault = Environment.GetEnvironmentVariable("CWI_POLICY_MODE_DEFAULT") ?? "standard";
        var defaultTenantId = Environment.GetEnvironmentVariable("AUTH_DEFAULT_TENANT") ??
            Environment.GetEnvironmentVariable("CWI_TENANT_ID");
        var orchestratorTimeoutSeconds = ParseInt("ORCHESTRATOR_TIMEOUT_SECONDS", defaultValue: 15);
        var orchestratorClientCertPath = Environment.GetEnvironmentVariable("ORCHESTRATOR_CLIENT_CERT_PATH");
        var orchestratorClientCertPassword = Environment.GetEnvironmentVariable("ORCHESTRATOR_CLIENT_CERT_PASSWORD");
        var orchestratorCaPath = Environment.GetEnvironmentVariable("ORCHESTRATOR_MTLS_CA_PATH");

        return new RouterConfig(
            httpPort,
            orchestratorUrl,
            orchestratorUrls,
            coordinationPrimary,
            coordinationLayerUrls,
            jwksUrl,
            jwtIssuer,
            jwtAudience,
            identityUrl,
            tokenEpochCacheSeconds,
            authRequired,
            mtlsRequired,
            redisUrl,
            rateLimit,
            rateWindowSeconds,
            modelProEndpoints,
            modelFreeEndpoints,
            modelProCapabilitiesEndpoints,
            modelFreeCapabilitiesEndpoints,
            modelHealthPath,
            modelHealthProbeIntervalSeconds,
            modelCircuitBreakerFailureThreshold,
            modelCircuitBreakerCooldownSeconds,
            policyModeDefault,
            defaultTenantId,
            orchestratorTimeoutSeconds,
            orchestratorClientCertPath,
            orchestratorClientCertPassword,
            orchestratorCaPath);
    }

    private static string RequireEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required env var: {name}");
        }

        return value;
    }

    private static bool ParseBool(string name, bool defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"{name} must be 'true' or 'false'.");
    }

    private static int ParseInt(string name, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (int.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"{name} must be an integer.");
    }

    private static IReadOnlyList<string> ParseEndpointList(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(endpoint => endpoint.Trim())
            .Where(endpoint => endpoint.Length > 0)
            .ToArray();
    }
}
