using Cwi.Orchestrator.Application.Packs;

namespace Cwi.Orchestrator.Application;

public sealed class ApplicationOptions
{
    public string PacksBaseUrl { get; }
    public string? PacksDirectory { get; }
    public bool GraphEnabled { get; }
    public string? GraphDbConnectionString { get; }
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
    public PackResolverMode PackResolverMode { get; }
    public string? PacksAdoptedBaseUrl { get; }
    public string PacksAdoptedManifestPath { get; }
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
    public string? CoreCommunicationPackRef { get; }
    public string? CoreIdentityPackRef { get; }

    public ApplicationOptions(
        string packsBaseUrl,
        string? packsDirectory,
        bool graphEnabled,
        string? graphDbConnectionString,
        string graphName,
        int graphQueryTimeoutSeconds,
        int graphTopK,
        string vectorShard1Url,
        string vectorShard2Url,
        int vectorEmbeddingDimension,
        string modelBaseUrl,
        string modelName,
        int modelTimeoutSeconds,
        string policyVersion,
        string ledgerHashSuiteId,
        string? modelBaseUrlPro = null,
        string? modelBaseUrlFree = null,
        string? modelNamePro = null,
        string? modelNameFree = null,
        PackResolverMode packResolverMode = PackResolverMode.Auto,
        string? packsAdoptedBaseUrl = null,
        string? packsAdoptedManifestPath = null,
        string? coordinationBaseUrl = null,
        string? coordinationProtocolVersion = null,
        string? coordinationAdminHeaderName = null,
        string? coordinationAdminToken = null,
        string? homeNodeWorkTokenIssuer = null,
        string? homeNodeWorkTokenSecret = null,
        int homeNodeWorkTokenTtlSeconds = 120,
        int homeNodeHeartbeatMaxAgeSeconds = 120,
        int homeNodeImmediateWaitTimeoutSeconds = 10,
        int homeNodeImmediatePollIntervalMilliseconds = 250,
        IReadOnlyList<string>? baselinePackNames = null,
        int baselinePackContentMaxChars = 8000,
        CorePackRefreshMode corePackRefreshMode = CorePackRefreshMode.Off,
        int corePackRefreshIntervalSeconds = 30,
        string? coreCommunicationPackRef = null,
        string? coreIdentityPackRef = null,
        int modelMaxOutputTokens = 384)
    {
        PacksBaseUrl = packsBaseUrl;
        PacksDirectory = packsDirectory;
        GraphEnabled = graphEnabled;
        GraphDbConnectionString = string.IsNullOrWhiteSpace(graphDbConnectionString) ? null : graphDbConnectionString;
        GraphName = string.IsNullOrWhiteSpace(graphName) ? "cwi_graph" : graphName.Trim();
        GraphQueryTimeoutSeconds = graphQueryTimeoutSeconds <= 0 ? 5 : graphQueryTimeoutSeconds;
        GraphTopK = graphTopK <= 0 ? 5 : graphTopK;
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
        ModelMaxOutputTokens = modelMaxOutputTokens <= 0 ? 384 : modelMaxOutputTokens;
        PolicyVersion = policyVersion;
        LedgerHashSuiteId = ledgerHashSuiteId;
        PackResolverMode = packResolverMode;
        PacksAdoptedBaseUrl = packsAdoptedBaseUrl;
        PacksAdoptedManifestPath = string.IsNullOrWhiteSpace(packsAdoptedManifestPath)
            ? "/packs/adopted/current_adoption.json"
            : packsAdoptedManifestPath;
        CoordinationBaseUrl = string.IsNullOrWhiteSpace(coordinationBaseUrl) ? null : coordinationBaseUrl.Trim();
        CoordinationProtocolVersion = string.IsNullOrWhiteSpace(coordinationProtocolVersion)
            ? "1.0"
            : coordinationProtocolVersion.Trim();
        CoordinationAdminHeaderName = string.IsNullOrWhiteSpace(coordinationAdminHeaderName)
            ? "X-Admin-Token"
            : coordinationAdminHeaderName.Trim();
        CoordinationAdminToken = string.IsNullOrWhiteSpace(coordinationAdminToken) ? null : coordinationAdminToken.Trim();
        HomeNodeWorkTokenIssuer = string.IsNullOrWhiteSpace(homeNodeWorkTokenIssuer)
            ? "cwi-orchestrator"
            : homeNodeWorkTokenIssuer.Trim();
        HomeNodeWorkTokenSecret = string.IsNullOrWhiteSpace(homeNodeWorkTokenSecret) ? null : homeNodeWorkTokenSecret;
        HomeNodeWorkTokenTtlSeconds = homeNodeWorkTokenTtlSeconds <= 0 ? 120 : homeNodeWorkTokenTtlSeconds;
        HomeNodeHeartbeatMaxAgeSeconds = homeNodeHeartbeatMaxAgeSeconds <= 0 ? 120 : homeNodeHeartbeatMaxAgeSeconds;
        HomeNodeImmediateWaitTimeoutSeconds = homeNodeImmediateWaitTimeoutSeconds <= 0 ? 10 : homeNodeImmediateWaitTimeoutSeconds;
        HomeNodeImmediatePollIntervalMilliseconds = homeNodeImmediatePollIntervalMilliseconds <= 0 ? 250 : homeNodeImmediatePollIntervalMilliseconds;
        BaselinePackNames = baselinePackNames?
            .Select(pack => pack.Trim())
            .Where(pack => !string.IsNullOrWhiteSpace(pack))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();
        BaselinePackContentMaxChars = baselinePackContentMaxChars <= 0 ? 8000 : baselinePackContentMaxChars;
        CorePackRefreshMode = corePackRefreshMode;
        CorePackRefreshIntervalSeconds = corePackRefreshIntervalSeconds <= 0 ? 30 : corePackRefreshIntervalSeconds;
        CoreCommunicationPackRef = string.IsNullOrWhiteSpace(coreCommunicationPackRef) ? "latest" : coreCommunicationPackRef.Trim();
        CoreIdentityPackRef = string.IsNullOrWhiteSpace(coreIdentityPackRef) ? "latest" : coreIdentityPackRef.Trim();
    }
}
