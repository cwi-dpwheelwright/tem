using System.Security.Cryptography.X509Certificates;
using Cwi.Common.Events;
using Cwi.Orchestrator.Api.Journey;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Api.Configuration;
using Cwi.Orchestrator.Api.Trace;
using Cwi.Orchestrator.Application.Policy;
using Cwi.Orchestrator.Composition;
using Microsoft.AspNetCore.Server.Kestrel.Https;

var builder = WebApplication.CreateBuilder(args);
var config = OrchestratorConfig.LoadFromEnvironment();
var traceQueryOptions = TraceQueryOptions.LoadFromEnvironment();
var journeyStreamOptions = JourneyStreamOptions.LoadFromEnvironment();
var killSwitchOptions = LoadKillSwitchOptions();
var authOptions = AuthOptions.LoadFromEnvironment();
var corsOrigins = (Environment.GetEnvironmentVariable("ORCHESTRATOR_CORS_ORIGINS") ?? string.Empty)
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton(traceQueryOptions);
builder.Services.AddSingleton(journeyStreamOptions);
builder.Services.AddSingleton(killSwitchOptions);
builder.Services.AddSingleton<IJwtValidator, JwtValidator>();
builder.Services.AddSingleton<TraceSpanStore>();
builder.Services.AddSingleton<JourneyBroadcaster>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
if (corsOrigins.Length > 0)
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("orchestrator-configured-origins", policy =>
        {
            policy.WithOrigins(corsOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
    });
}
builder.Services.AddScoped<Cwi.Orchestrator.Api.Health.DependencyHealthChecker>();
builder.Services.AddEventBusClient();
if (traceQueryOptions.Enabled)
{
    builder.Services.AddHostedService<TraceSpanListener>();
}
if (journeyStreamOptions.Enabled)
{
    builder.Services.AddHostedService<JourneyStreamIngestHostedService>();
}

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(config.HttpPort);

    if (config.HttpsPort <= 0)
    {
        return;
    }

    if (string.IsNullOrWhiteSpace(config.TlsCertPath))
    {
        throw new InvalidOperationException("ORCHESTRATOR_TLS_CERT_PATH is required when HTTPS is enabled.");
    }

    var serverCert = Program.LoadServerCertificate(config.TlsCertPath, config.TlsCertPassword);
    X509Certificate2? caCert = null;

    if (config.MtlsRequired)
    {
        if (string.IsNullOrWhiteSpace(config.MtlsCaPath))
        {
            throw new InvalidOperationException("ORCHESTRATOR_MTLS_CA_PATH is required when MTLS_REQUIRED is true.");
        }

        caCert = new X509Certificate2(config.MtlsCaPath);
    }

    options.ListenAnyIP(config.HttpsPort, listenOptions =>
    {
        listenOptions.UseHttps(httpsOptions =>
        {
            httpsOptions.ServerCertificate = serverCert;

            if (config.MtlsRequired)
            {
                httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                httpsOptions.ClientCertificateValidation = (certificate, _, _) =>
                    Program.ValidateClientCertificate(certificate as X509Certificate2, caCert);
            }
        });
    });
});

builder.Services.AddOrchestratorComposition(new OrchestratorCompositionOptions(
    config.PacksBaseUrl,
    config.PacksDirectory,
    config.DbConnectionString,
    config.GraphEnabled,
    config.GraphDbConnectionString,
    config.GraphName,
    config.GraphQueryTimeoutSeconds,
    config.GraphTopK,
    config.VectorShard1Url,
    config.VectorShard2Url,
    config.VectorEmbeddingDimension,
    config.ModelBaseUrl,
    config.ModelName,
    config.ModelTimeoutSeconds,
    config.PolicyVersion,
    config.LedgerHashSuiteId,
    config.ModelBaseUrlPro,
    config.ModelBaseUrlFree,
    config.ModelNamePro,
    config.ModelNameFree,
    config.ModelMaxOutputTokens,
    config.LedgerRedactionEnabled,
    config.LedgerRedactionFields,
    config.BreakGlassRoles,
    config.BreakGlassScopes,
    config.BreakGlassMaxMinutes,
    config.BreakGlassRequireReason,
    config.TelemetryIndexEnabled,
    config.TelemetryIndexMaxEntries,
    config.RetentionTelemetryDays,
    config.RetentionLedgerDays,
    config.RetentionTraceDays,
    config.RetentionPurgeEnabled,
    config.SimulationEnabled,
    config.SimulationTimeoutMs,
    config.GuardrailCorrectionMode,
    config.GuardrailCorrectionMaxSeverity,
    config.PackResolverMode,
    config.PacksAdoptedBaseUrl,
    config.PacksAdoptedManifestPath,
    config.CoordinationBaseUrl,
    config.CoordinationProtocolVersion,
    config.CoordinationAdminHeaderName,
    config.CoordinationAdminToken,
    config.HomeNodeWorkTokenIssuer,
    config.HomeNodeWorkTokenSecret,
    config.HomeNodeWorkTokenTtlSeconds,
    config.HomeNodeHeartbeatMaxAgeSeconds,
    config.HomeNodeImmediateWaitTimeoutSeconds,
    config.HomeNodeImmediatePollIntervalMilliseconds,
    config.BaselinePackNames,
    config.BaselinePackContentMaxChars,
    config.CorePackRefreshMode,
    config.CorePackRefreshIntervalSeconds,
    config.CoreCommunicationPackRef,
    config.CoreIdentityPackRef));

var app = builder.Build();
AttachEventBus(app);
if (app.Environment.IsDevelopment())
{
    app.UseSwagger(options =>
    {
        options.RouteTemplate = "api/dev/openapi/{documentName}.json";
    });
}
app.UseRouting();
if (corsOrigins.Length > 0)
{
    app.UseCors("orchestrator-configured-origins");
}
app.UseMiddleware<JwtAuthMiddleware>();
app.UseMiddleware<Cwi.Orchestrator.Api.BreakGlass.BreakGlassMiddleware>();
app.MapControllers();
app.Run();

void AttachEventBus(WebApplication app)
{
    var options = app.Services.GetRequiredService<EventBusClientOptions>();
    if (!options.Enabled)
    {
        return;
    }

    var client = app.Services.GetRequiredService<EventBusClient>();
    app.Lifetime.ApplicationStarted.Register(() =>
        _ = client.PublishAsync(new EventEnvelope
        {
            Type = "service.start",
            Polarity = "positive",
            Severity = "info",
            Message = "Orchestrator started."
        }));

    app.Lifetime.ApplicationStopping.Register(() =>
        _ = client.PublishAsync(new EventEnvelope
        {
            Type = "service.stop",
            Polarity = "negative",
            Severity = "warn",
            Message = "Orchestrator stopping."
        }));

    AppDomain.CurrentDomain.UnhandledException += (_, args) =>
    {
        if (args.ExceptionObject is Exception ex)
        {
            _ = client.PublishAsync(new EventEnvelope
            {
                Type = "service.error",
                Polarity = "negative",
                Severity = "error",
                Message = ex.Message,
                Data = new Dictionary<string, object?>
                {
                    ["stack"] = ex.StackTrace
                }
            });
        }
    };
}

public partial class Program
{
    public static X509Certificate2 LoadServerCertificate(string path, string? password)
    {
        return string.IsNullOrWhiteSpace(password)
            ? new X509Certificate2(path)
            : new X509Certificate2(path, password);
    }

    public static bool ValidateClientCertificate(X509Certificate2? certificate, X509Certificate2? caCertificate)
    {
        if (certificate is null || caCertificate is null)
        {
            return false;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(caCertificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

        return chain.Build(certificate);
    }

    private static KillSwitchAdminOptions LoadKillSwitchOptions()
    {
        return new KillSwitchAdminOptions
        {
            Enabled = ParseBool("KILL_SWITCH_ADMIN_ENABLED", true),
            KillCode = Environment.GetEnvironmentVariable("KILL_SWITCH_CODE"),
            TokenRequired = ParseBool("KILL_SWITCH_TOKEN_REQUIRED", false),
            TokenTtlSeconds = ParseInt("KILL_SWITCH_TOKEN_TTL_SECONDS", 300),
            TokenMaxTtlSeconds = ParseInt("KILL_SWITCH_TOKEN_MAX_TTL_SECONDS", 900)
        };
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

        return defaultValue;
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

        return defaultValue;
    }
}
