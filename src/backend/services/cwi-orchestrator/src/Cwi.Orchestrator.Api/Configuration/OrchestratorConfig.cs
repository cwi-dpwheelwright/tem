using Cwi.Orchestrator.Application.Packs;
using Npgsql;

namespace Cwi.Orchestrator.Api.Configuration;

public sealed class OrchestratorConfig
{
    private const string DefaultLedgerHashSuite = "sha256-v1";
    public string Registry { get; }
    public string Tag { get; }
    public int HttpPort { get; }
    public string PacksBaseUrl { get; }
    public string? PacksDirectory { get; }
    public PackResolverMode PackResolverMode { get; }
    public string? PacksAdoptedBaseUrl { get; }
    public string PacksAdoptedManifestPath { get; }
    public string DbConnectionString { get; }
    public bool GraphEnabled { get; }
    public string GraphDbConnectionString { get; }
    public string GraphName { get; }
    public int GraphQueryTimeoutSeconds { get; }
    public int GraphTopK { get; }
    public string VectorShard1Url { get; }
    public string VectorShard2Url { get; }
    public int VectorEmbeddingDimension { get; }
    public string ModelBaseUrl { get; }
    public string ModelName { get; }
    public string? ModelBaseUrlPro { get; }
    public string? ModelBaseUrlFree { get; }
    public string? ModelNamePro { get; }
    public string? ModelNameFree { get; }
    public int ModelTimeoutSeconds { get; }
    public int ModelMaxOutputTokens { get; }
    public string PolicyVersion { get; }
    public string LedgerHashSuiteId { get; }
    public bool LedgerRedactionEnabled { get; }
    public IReadOnlyList<string> LedgerRedactionFields { get; }
    public int HttpsPort { get; }
    public string? TlsCertPath { get; }
    public string? TlsCertPassword { get; }
    public bool MtlsRequired { get; }
    public string? MtlsCaPath { get; }
    public IReadOnlyList<string> BreakGlassRoles { get; }
    public IReadOnlyList<string> BreakGlassScopes { get; }
    public int BreakGlassMaxMinutes { get; }
    public bool BreakGlassRequireReason { get; }
    public bool TelemetryIndexEnabled { get; }
    public int TelemetryIndexMaxEntries { get; }
    public int RetentionTelemetryDays { get; }
    public int RetentionLedgerDays { get; }
    public int RetentionTraceDays { get; }
    public bool RetentionPurgeEnabled { get; }
    public bool SimulationEnabled { get; }
    public int SimulationTimeoutMs { get; }
    public string GuardrailCorrectionMode { get; }
    public string GuardrailCorrectionMaxSeverity { get; }
    public string? CoordinationBaseUrl { get; }
    public string CoordinationProtocolVersion { get; }
    public string CoordinationAdminHeaderName { get; }
    public string? CoordinationAdminToken { get; }
    public string HomeNodeWorkTokenIssuer { get; }
    public string? HomeNodeWorkTokenSecret { get; }
    public int HomeNodeWorkTokenTtlSeconds { get; }
    public int HomeNodeHeartbeatMaxAgeSeconds { get; }
    public int HomeNodeImmediateWaitTimeoutSeconds { get; }
    public int HomeNodeImmediatePollIntervalMilliseconds { get; }
    public IReadOnlyList<string> BaselinePackNames { get; }
    public int BaselinePackContentMaxChars { get; }
    public CorePackRefreshMode CorePackRefreshMode { get; }
    public int CorePackRefreshIntervalSeconds { get; }
    public string CoreCommunicationPackRef { get; }
    public string CoreIdentityPackRef { get; }

    private OrchestratorConfig(
        string registry,
        string tag,
        int httpPort,
        string packsBaseUrl,
        string? packsDirectory,
        PackResolverMode packResolverMode,
        string? packsAdoptedBaseUrl,
        string packsAdoptedManifestPath,
        string dbConnectionString,
        bool graphEnabled,
        string graphDbConnectionString,
        string graphName,
        int graphQueryTimeoutSeconds,
        int graphTopK,
        string vectorShard1Url,
        string vectorShard2Url,
        int vectorEmbeddingDimension,
        string modelBaseUrl,
        string modelName,
        string? modelBaseUrlPro,
        string? modelBaseUrlFree,
        string? modelNamePro,
        string? modelNameFree,
        int modelTimeoutSeconds,
        int modelMaxOutputTokens,
        string policyVersion,
        string ledgerHashSuiteId,
        bool ledgerRedactionEnabled,
        IReadOnlyList<string> ledgerRedactionFields,
        int httpsPort,
        string? tlsCertPath,
        string? tlsCertPassword,
        bool mtlsRequired,
        string? mtlsCaPath,
        IReadOnlyList<string> breakGlassRoles,
        IReadOnlyList<string> breakGlassScopes,
        int breakGlassMaxMinutes,
        bool breakGlassRequireReason,
        bool telemetryIndexEnabled,
        int telemetryIndexMaxEntries,
        int retentionTelemetryDays,
        int retentionLedgerDays,
        int retentionTraceDays,
        bool retentionPurgeEnabled,
        bool simulationEnabled,
        int simulationTimeoutMs,
        string guardrailCorrectionMode,
        string guardrailCorrectionMaxSeverity,
        string? coordinationBaseUrl,
        string coordinationProtocolVersion,
        string coordinationAdminHeaderName,
        string? coordinationAdminToken,
        string homeNodeWorkTokenIssuer,
        string? homeNodeWorkTokenSecret,
        int homeNodeWorkTokenTtlSeconds,
        int homeNodeHeartbeatMaxAgeSeconds,
        int homeNodeImmediateWaitTimeoutSeconds,
        int homeNodeImmediatePollIntervalMilliseconds,
        IReadOnlyList<string> baselinePackNames,
        int baselinePackContentMaxChars,
        CorePackRefreshMode corePackRefreshMode,
        int corePackRefreshIntervalSeconds,
        string coreCommunicationPackRef,
        string coreIdentityPackRef)
    {
        Registry = registry;
        Tag = tag;
        HttpPort = httpPort;
        PacksBaseUrl = packsBaseUrl;
        PacksDirectory = packsDirectory;
        PackResolverMode = packResolverMode;
        PacksAdoptedBaseUrl = packsAdoptedBaseUrl;
        PacksAdoptedManifestPath = packsAdoptedManifestPath;
        DbConnectionString = dbConnectionString;
        GraphEnabled = graphEnabled;
        GraphDbConnectionString = graphDbConnectionString;
        GraphName = graphName;
        GraphQueryTimeoutSeconds = graphQueryTimeoutSeconds;
        GraphTopK = graphTopK;
        VectorShard1Url = vectorShard1Url;
        VectorShard2Url = vectorShard2Url;
        VectorEmbeddingDimension = vectorEmbeddingDimension;
        ModelBaseUrl = modelBaseUrl;
        ModelName = modelName;
        ModelBaseUrlPro = modelBaseUrlPro;
        ModelBaseUrlFree = modelBaseUrlFree;
        ModelNamePro = modelNamePro;
        ModelNameFree = modelNameFree;
        ModelTimeoutSeconds = modelTimeoutSeconds;
        ModelMaxOutputTokens = modelMaxOutputTokens;
        PolicyVersion = policyVersion;
        LedgerHashSuiteId = ledgerHashSuiteId;
        LedgerRedactionEnabled = ledgerRedactionEnabled;
        LedgerRedactionFields = ledgerRedactionFields;
        HttpsPort = httpsPort;
        TlsCertPath = tlsCertPath;
        TlsCertPassword = tlsCertPassword;
        MtlsRequired = mtlsRequired;
        MtlsCaPath = mtlsCaPath;
        BreakGlassRoles = breakGlassRoles;
        BreakGlassScopes = breakGlassScopes;
        BreakGlassMaxMinutes = breakGlassMaxMinutes;
        BreakGlassRequireReason = breakGlassRequireReason;
        TelemetryIndexEnabled = telemetryIndexEnabled;
        TelemetryIndexMaxEntries = telemetryIndexMaxEntries;
        RetentionTelemetryDays = retentionTelemetryDays;
        RetentionLedgerDays = retentionLedgerDays;
        RetentionTraceDays = retentionTraceDays;
        RetentionPurgeEnabled = retentionPurgeEnabled;
        SimulationEnabled = simulationEnabled;
        SimulationTimeoutMs = simulationTimeoutMs;
        GuardrailCorrectionMode = guardrailCorrectionMode;
        GuardrailCorrectionMaxSeverity = guardrailCorrectionMaxSeverity;
        CoordinationBaseUrl = coordinationBaseUrl;
        CoordinationProtocolVersion = coordinationProtocolVersion;
        CoordinationAdminHeaderName = coordinationAdminHeaderName;
        CoordinationAdminToken = coordinationAdminToken;
        HomeNodeWorkTokenIssuer = homeNodeWorkTokenIssuer;
        HomeNodeWorkTokenSecret = homeNodeWorkTokenSecret;
        HomeNodeWorkTokenTtlSeconds = homeNodeWorkTokenTtlSeconds;
        HomeNodeHeartbeatMaxAgeSeconds = homeNodeHeartbeatMaxAgeSeconds;
        HomeNodeImmediateWaitTimeoutSeconds = homeNodeImmediateWaitTimeoutSeconds;
        HomeNodeImmediatePollIntervalMilliseconds = homeNodeImmediatePollIntervalMilliseconds;
        BaselinePackNames = baselinePackNames;
        BaselinePackContentMaxChars = baselinePackContentMaxChars;
        CorePackRefreshMode = corePackRefreshMode;
        CorePackRefreshIntervalSeconds = corePackRefreshIntervalSeconds;
        CoreCommunicationPackRef = coreCommunicationPackRef;
        CoreIdentityPackRef = coreIdentityPackRef;
    }

    public static OrchestratorConfig LoadFromEnvironment()
    {
        var registry = RequireEnv("REGISTRY");
        var tag = RequireEnv("CWI_ORCHESTRATOR_TAG");
        var portRaw = RequireEnv("ORCHESTRATOR_HTTP_PORT");
        var packsBaseUrl = RequireEnv("PACKS_BASE_URL");
        var packsDirectoryRaw = Environment.GetEnvironmentVariable("PACKS_DIR");
        var packsDirectory = string.IsNullOrWhiteSpace(packsDirectoryRaw) ? null : packsDirectoryRaw;
        var packResolverMode = ParseResolverMode(Environment.GetEnvironmentVariable("PACK_RESOLVER_MODE"));
        var packsAdoptedBaseUrlRaw = Environment.GetEnvironmentVariable("PACKS_ADOPTED_BASE_URL");
        var packsAdoptedBaseUrl = string.IsNullOrWhiteSpace(packsAdoptedBaseUrlRaw) ? null : packsAdoptedBaseUrlRaw;
        var packsAdoptedManifestPath = Environment.GetEnvironmentVariable("PACKS_ADOPTED_MANIFEST_PATH")
            ?? "/packs/adopted/current_adoption.json";
        var dbConnectionString = RequireEnv("ORCHESTRATOR_DB_CONNECTION");
        var graphEnabled = ParseBool("CWI_GRAPH_ENABLED", defaultValue: false);
        var graphDbConnectionStringRaw = RequireEnv("CWI_GRAPH_DB_DSN");
        var graphDbConnectionString = NormalizeGraphDbConnectionString(graphDbConnectionStringRaw);
        var graphName = NormalizeGraphName(
            Environment.GetEnvironmentVariable("CWI_GRAPH_NAME")
            ?? Environment.GetEnvironmentVariable("CWI_GRAPH_DB_NAME")
            ?? "cwi_graph");
        var graphQueryTimeoutSeconds = ParseInt("CWI_GRAPH_QUERY_TIMEOUT_SECONDS", defaultValue: 5);
        var graphTopK = ParseInt("CWI_GRAPH_TOP_K", defaultValue: 5);
        var vectorShard1Url = RequireEnv("VECTOR_SHARD_1_URL");
        var vectorShard2Url = RequireEnv("VECTOR_SHARD_2_URL");
        var vectorDimensionRaw = RequireEnv("VECTOR_EMBEDDING_DIMENSION");
        var modelBaseUrl = RequireEnv("MODEL_BASE_URL");
        var modelName = RequireEnv("MODEL_NAME");
        var modelBaseUrlPro = NormalizeOptional(Environment.GetEnvironmentVariable("MODEL_BASE_URL_PRO"));
        var modelBaseUrlFree = NormalizeOptional(Environment.GetEnvironmentVariable("MODEL_BASE_URL_FREE"));
        var modelNamePro = NormalizeOptional(Environment.GetEnvironmentVariable("MODEL_NAME_PRO"));
        var modelNameFree = NormalizeOptional(Environment.GetEnvironmentVariable("MODEL_NAME_FREE"));
        var modelTimeoutRaw = RequireEnv("MODEL_TIMEOUT_SECONDS");
        var modelMaxOutputTokens = ParseInt("MODEL_MAX_OUTPUT_TOKENS", defaultValue: 384);
        var policyVersion = Environment.GetEnvironmentVariable("POLICY_VERSION") ?? "v1";
        var ledgerHashSuiteId = Environment.GetEnvironmentVariable("LEDGER_HASH_SUITE") ?? DefaultLedgerHashSuite;
        var ledgerRedactionEnabled = ParseBool("LEDGER_REDACTION_ENABLED", defaultValue: false);
        var ledgerRedactionFields = ParseCsv("LEDGER_REDACTION_FIELDS");
        var httpsPort = ParseInt("ORCHESTRATOR_HTTPS_PORT", defaultValue: 0);
        var tlsCertPath = Environment.GetEnvironmentVariable("ORCHESTRATOR_TLS_CERT_PATH");
        var tlsCertPassword = Environment.GetEnvironmentVariable("ORCHESTRATOR_TLS_CERT_PASSWORD");
        var mtlsRequired = ParseBool("MTLS_REQUIRED", defaultValue: false);
        var mtlsCaPath = Environment.GetEnvironmentVariable("ORCHESTRATOR_MTLS_CA_PATH");
        var breakGlassRoles = ParseCsv("BREAK_GLASS_ROLES");
        var breakGlassScopes = ParseCsv("BREAK_GLASS_SCOPES");
        var breakGlassMaxMinutes = ParseInt("BREAK_GLASS_MAX_MINUTES", defaultValue: 60);
        var breakGlassRequireReason = ParseBool("BREAK_GLASS_REQUIRE_REASON", defaultValue: true);
        var telemetryIndexEnabled = ParseBool("TELEMETRY_INDEX_ENABLED", defaultValue: false);
        var telemetryIndexMaxEntries = ParseInt("TELEMETRY_INDEX_MAX_ENTRIES", defaultValue: 1000);
        var retentionTelemetryDays = ParseInt("RETENTION_TELEMETRY_DAYS", defaultValue: 0);
        var retentionLedgerDays = ParseInt("RETENTION_LEDGER_DAYS", defaultValue: 0);
        var retentionTraceDays = ParseInt("RETENTION_TRACE_DAYS", defaultValue: 0);
        var retentionPurgeEnabled = ParseBool("RETENTION_PURGE_ENABLED", defaultValue: false);
        var simulationEnabled = ParseBool("SIMULATION_ENABLED", defaultValue: true);
        var simulationTimeoutMs = ParseInt("SIMULATION_TIMEOUT_MS", defaultValue: 250);
        var guardrailCorrectionMode = Environment.GetEnvironmentVariable("GUARDRAIL_CORRECTION_MODE") ?? "auto_correct";
        var guardrailCorrectionMaxSeverity = Environment.GetEnvironmentVariable("GUARDRAIL_CORRECTION_MAX_SEVERITY") ?? "medium";
        var coordinationBaseUrl = NormalizeOptional(Environment.GetEnvironmentVariable("COORDINATION_BASE_URL"));
        var coordinationProtocolVersion = Environment.GetEnvironmentVariable("COORDINATION_PROTOCOL_VERSION") ?? "1.0";
        var coordinationAdminHeaderName = Environment.GetEnvironmentVariable("COORDINATION_ADMIN_HEADER_NAME") ?? "X-Admin-Token";
        var coordinationAdminToken = NormalizeOptional(Environment.GetEnvironmentVariable("COORDINATION_ADMIN_TOKEN"));
        var homeNodeWorkTokenIssuer = Environment.GetEnvironmentVariable("HOME_NODE_WORK_TOKEN_ISSUER") ?? "cwi-orchestrator";
        var homeNodeWorkTokenSecret = NormalizeOptional(Environment.GetEnvironmentVariable("HOME_NODE_WORK_TOKEN_SECRET"));
        var homeNodeWorkTokenTtlSeconds = ParseInt("HOME_NODE_WORK_TOKEN_TTL_SECONDS", defaultValue: 120);
        var homeNodeHeartbeatMaxAgeSeconds = ParseInt("HOME_NODE_HEARTBEAT_MAX_AGE_SECONDS", defaultValue: 120);
        var homeNodeImmediateWaitTimeoutSeconds = ParseInt("HOME_NODE_WAIT_TIMEOUT_SECONDS", defaultValue: 10);
        var homeNodeImmediatePollIntervalMilliseconds = ParseInt("HOME_NODE_WAIT_POLL_INTERVAL_MS", defaultValue: 250);
        var baselinePackNames = CorePackOrdering.MergeRequiredPackNames(ParseCsv("CWI_BASELINE_PACKS"));
        var baselinePackContentMaxChars = ParseInt("CWI_BASELINE_PACK_CONTENT_MAX_CHARS", defaultValue: 8000);
        var corePackRefreshMode = ParseCorePackRefreshMode(Environment.GetEnvironmentVariable("CORE_PACK_REFRESH_MODE"));
        var corePackRefreshIntervalSeconds = ParseInt("CORE_PACK_REFRESH_INTERVAL_SECONDS", defaultValue: 30);
        var coreCommunicationPackRef = Environment.GetEnvironmentVariable("CORE_COMMUNICATION_PACK_REF") ?? "latest";
        var coreIdentityPackRef = Environment.GetEnvironmentVariable("CORE_IDENTITY_PACK_REF") ?? "latest";

        if (!int.TryParse(portRaw, out var httpPort))
        {
            throw new InvalidOperationException("ORCHESTRATOR_HTTP_PORT must be an integer.");
        }

        if (!int.TryParse(vectorDimensionRaw, out var vectorEmbeddingDimension))
        {
            throw new InvalidOperationException("VECTOR_EMBEDDING_DIMENSION must be an integer.");
        }

        if (!int.TryParse(modelTimeoutRaw, out var modelTimeoutSeconds))
        {
            throw new InvalidOperationException("MODEL_TIMEOUT_SECONDS must be an integer.");
        }

        return new OrchestratorConfig(
            registry,
            tag,
            httpPort,
            packsBaseUrl,
            packsDirectory,
            packResolverMode,
            packsAdoptedBaseUrl,
            packsAdoptedManifestPath,
            dbConnectionString,
            graphEnabled,
            graphDbConnectionString,
            graphName,
            graphQueryTimeoutSeconds,
            graphTopK,
            vectorShard1Url,
            vectorShard2Url,
            vectorEmbeddingDimension,
            modelBaseUrl,
            modelName,
            modelBaseUrlPro,
            modelBaseUrlFree,
            modelNamePro,
            modelNameFree,
            modelTimeoutSeconds,
            modelMaxOutputTokens,
            policyVersion,
            ledgerHashSuiteId,
            ledgerRedactionEnabled,
            ledgerRedactionFields,
            httpsPort,
            tlsCertPath,
            tlsCertPassword,
            mtlsRequired,
            mtlsCaPath,
            breakGlassRoles,
            breakGlassScopes,
            breakGlassMaxMinutes,
            breakGlassRequireReason,
            telemetryIndexEnabled,
            telemetryIndexMaxEntries,
            retentionTelemetryDays,
            retentionLedgerDays,
            retentionTraceDays,
            retentionPurgeEnabled,
            simulationEnabled,
            simulationTimeoutMs,
            guardrailCorrectionMode,
            guardrailCorrectionMaxSeverity,
            coordinationBaseUrl,
            coordinationProtocolVersion,
            coordinationAdminHeaderName,
            coordinationAdminToken,
            homeNodeWorkTokenIssuer,
            homeNodeWorkTokenSecret,
            homeNodeWorkTokenTtlSeconds,
            homeNodeHeartbeatMaxAgeSeconds,
            homeNodeImmediateWaitTimeoutSeconds,
            homeNodeImmediatePollIntervalMilliseconds,
            baselinePackNames,
            baselinePackContentMaxChars,
            corePackRefreshMode,
            corePackRefreshIntervalSeconds,
            coreCommunicationPackRef,
            coreIdentityPackRef);
    }

    private static PackResolverMode ParseResolverMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PackResolverMode.Auto;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "legacy" or "legacy_only" => PackResolverMode.LegacyOnly,
            "adopted" or "adopted_only" => PackResolverMode.AdoptedOnly,
            "legacy_first" => PackResolverMode.LegacyFirst,
            "adopted_first" => PackResolverMode.AdoptedFirst,
            "auto" => PackResolverMode.Auto,
            _ => throw new InvalidOperationException("PACK_RESOLVER_MODE must be one of: auto, legacy, adopted, legacy_first, adopted_first.")
        };
    }

    private static CorePackRefreshMode ParseCorePackRefreshMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CorePackRefreshMode.Off;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "watch" => CorePackRefreshMode.Watch,
            "interval" or "poll" => CorePackRefreshMode.Interval,
            "off" or "disabled" or "manual" => CorePackRefreshMode.Off,
            _ => CorePackRefreshMode.Off
        };
    }

    private static string NormalizeGraphDbConnectionString(string value)
    {
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            return value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("CWI_GRAPH_DB_DSN must be a valid URI.");
        }

        var userInfo = uri.UserInfo.Split(':', 2, StringSplitOptions.None);
        var username = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrWhiteSpace(database))
        {
            throw new InvalidOperationException("CWI_GRAPH_DB_DSN must include a database name.");
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Username = username,
            Password = password,
            Database = database
        };

        return builder.ConnectionString;
    }

    private static string NormalizeGraphName(string value)
    {
        var candidate = value.Trim();
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "cwi_graph";
        }

        foreach (var ch in candidate)
        {
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
            {
                throw new InvalidOperationException("CWI_GRAPH_NAME must be alphanumeric/underscore.");
            }
        }

        if (!(char.IsLetter(candidate[0]) || candidate[0] == '_'))
        {
            throw new InvalidOperationException("CWI_GRAPH_NAME must start with a letter or underscore.");
        }

        return candidate;
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

    private static IReadOnlyList<string> ParseCsv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
