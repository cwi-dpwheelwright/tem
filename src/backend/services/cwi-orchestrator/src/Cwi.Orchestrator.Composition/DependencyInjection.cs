using Cwi.Orchestrator.Application;
using Cwi.Orchestrator.Application.Retention;
using Cwi.Orchestrator.Application.Telemetry;
using Cwi.Orchestrator.Composition.Retention;
using Cwi.Orchestrator.DataAccess;
using Microsoft.Extensions.DependencyInjection;

namespace Cwi.Orchestrator.Composition;

public sealed record OrchestratorCompositionOptions(
    string PacksBaseUrl,
    string? PacksDirectory,
    string DbConnectionString,
    bool GraphEnabled,
    string GraphDbConnectionString,
    string GraphName,
    int GraphQueryTimeoutSeconds,
    int GraphTopK,
    string VectorShard1Url,
    string VectorShard2Url,
    int VectorEmbeddingDimension,
    string ModelBaseUrl,
    string ModelName,
    int ModelTimeoutSeconds,
    string PolicyVersion,
    string LedgerHashSuiteId,
    string? ModelBaseUrlPro,
    string? ModelBaseUrlFree,
    string? ModelNamePro,
    string? ModelNameFree,
    int ModelMaxOutputTokens,
    bool LedgerRedactionEnabled,
    IReadOnlyList<string> LedgerRedactionFields,
    IReadOnlyList<string> BreakGlassRoles,
    IReadOnlyList<string> BreakGlassScopes,
    int BreakGlassMaxMinutes,
    bool BreakGlassRequireReason,
    bool TelemetryIndexEnabled,
    int TelemetryIndexMaxEntries,
    int RetentionTelemetryDays,
    int RetentionLedgerDays,
    int RetentionTraceDays,
    bool RetentionPurgeEnabled,
    bool SimulationEnabled,
    int SimulationTimeoutMs,
    string GuardrailCorrectionMode,
    string GuardrailCorrectionMaxSeverity,
    Cwi.Orchestrator.Application.Packs.PackResolverMode PackResolverMode,
    string? PacksAdoptedBaseUrl,
    string? PacksAdoptedManifestPath,
    string? CoordinationBaseUrl,
    string CoordinationProtocolVersion,
    string CoordinationAdminHeaderName,
    string? CoordinationAdminToken,
    string HomeNodeWorkTokenIssuer,
    string? HomeNodeWorkTokenSecret,
    int HomeNodeWorkTokenTtlSeconds,
    int HomeNodeHeartbeatMaxAgeSeconds,
    int HomeNodeImmediateWaitTimeoutSeconds,
    int HomeNodeImmediatePollIntervalMilliseconds,
    IReadOnlyList<string> BaselinePackNames,
    int BaselinePackContentMaxChars,
    Cwi.Orchestrator.Application.Packs.CorePackRefreshMode CorePackRefreshMode,
    int CorePackRefreshIntervalSeconds,
    string? CoreCommunicationPackRef,
    string? CoreIdentityPackRef);

public static class DependencyInjection
{
    public static IServiceCollection AddOrchestratorComposition(
        this IServiceCollection services,
        OrchestratorCompositionOptions options)
    {
        services.AddOrchestratorApplication(new ApplicationOptions(
            options.PacksBaseUrl,
            options.PacksDirectory,
            options.GraphEnabled,
            options.GraphDbConnectionString,
            options.GraphName,
            options.GraphQueryTimeoutSeconds,
            options.GraphTopK,
            options.VectorShard1Url,
            options.VectorShard2Url,
            options.VectorEmbeddingDimension,
            options.ModelBaseUrl,
            options.ModelName,
            options.ModelTimeoutSeconds,
            options.PolicyVersion,
            options.LedgerHashSuiteId,
            options.ModelBaseUrlPro,
            options.ModelBaseUrlFree,
            options.ModelNamePro,
            options.ModelNameFree,
            options.PackResolverMode,
            options.PacksAdoptedBaseUrl,
            options.PacksAdoptedManifestPath,
            options.CoordinationBaseUrl,
            options.CoordinationProtocolVersion,
            options.CoordinationAdminHeaderName,
            options.CoordinationAdminToken,
            options.HomeNodeWorkTokenIssuer,
            options.HomeNodeWorkTokenSecret,
            options.HomeNodeWorkTokenTtlSeconds,
            options.HomeNodeHeartbeatMaxAgeSeconds,
            options.HomeNodeImmediateWaitTimeoutSeconds,
            options.HomeNodeImmediatePollIntervalMilliseconds,
            options.BaselinePackNames,
            options.BaselinePackContentMaxChars,
            options.CorePackRefreshMode,
            options.CorePackRefreshIntervalSeconds,
            options.CoreCommunicationPackRef,
            options.CoreIdentityPackRef,
            options.ModelMaxOutputTokens));
        services.AddOrchestratorDataAccess(options.DbConnectionString);

        services.AddSingleton(new Cwi.Orchestrator.Application.BreakGlass.BreakGlassPolicy
        {
            AllowedRoles = options.BreakGlassRoles,
            AllowedScopes = options.BreakGlassScopes,
            MaxDurationMinutes = options.BreakGlassMaxMinutes,
            RequireReason = options.BreakGlassRequireReason
        });
        services.AddSingleton(new TelemetryIndexOptions
        {
            Enabled = options.TelemetryIndexEnabled,
            MaxEntries = options.TelemetryIndexMaxEntries
        });
        services.AddSingleton(new RetentionPolicyOptions
        {
            TelemetryDays = options.RetentionTelemetryDays,
            LedgerDays = options.RetentionLedgerDays,
            TraceDays = options.RetentionTraceDays,
            PurgeEnabled = options.RetentionPurgeEnabled
        });
        services.AddSingleton(new Cwi.Orchestrator.Application.Provenance.LedgerRedactionOptions
        {
            Enabled = options.LedgerRedactionEnabled,
            Fields = options.LedgerRedactionFields
        });
        services.AddSingleton(new Cwi.Orchestrator.Application.Simulation.SimulationOptions
        {
            Enabled = options.SimulationEnabled,
            TimeoutMs = options.SimulationTimeoutMs
        });
        services.AddSingleton(new Cwi.Orchestrator.Application.Guardrails.Governance.GuardrailCorrectionOptions
        {
            Mode = options.GuardrailCorrectionMode,
            MaxSeverity = options.GuardrailCorrectionMaxSeverity
        });
        services.AddSingleton<Cwi.Orchestrator.Application.BreakGlass.BreakGlassPolicyValidator>();
        services.AddTransient<Cwi.Orchestrator.Application.BreakGlass.BreakGlassProvenanceRecorder>();
        services.AddSingleton<Cwi.Orchestrator.Application.BreakGlass.BreakGlassDenylistCache>();
        services.AddTransient<Cwi.Orchestrator.Application.BreakGlass.BreakGlassTokenValidator>();
        services.AddTransient<Cwi.Orchestrator.Application.BreakGlass.BreakGlassService>();
        services.AddTransient<Cwi.Orchestrator.Application.BreakGlass.BreakGlassAuditService>();
        services.AddTransient<Cwi.Orchestrator.Application.BreakGlass.IBreakGlassNotifier, Cwi.Orchestrator.Application.BreakGlass.BreakGlassNotificationRecorder>();
        services.AddTransient<Cwi.Orchestrator.Application.Packs.Activation.PackActivationAuditRecorder>();
        services.AddTransient<Cwi.Orchestrator.Application.Packs.Activation.PackActivationService>();

        services.AddScoped<RetentionPurgeService>();
        services.AddHostedService<RetentionPurgeHostedService>();
        return services;
    }
}
