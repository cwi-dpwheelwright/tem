using Cwi.Common.Events;
using Cwi.Router.Application.Configuration;
using Cwi.Router.Application.Clients;
using Cwi.Router.Application.EventBus;
using Cwi.Router.Application.Handlers;
using Cwi.Router.Application.Health;
using Cwi.Router.Application.Journey;
using Cwi.Router.Application.Metrics;
using Cwi.Router.Application.Pipeline;
using Cwi.Router.Application.Routing;
using Cwi.Router.Application.Survival;
using Cwi.Router.Api.Trace;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);
var config = RouterConfig.LoadFromEnvironment();
var traceQueryOptions = TraceQueryOptions.LoadFromEnvironment();
var routingDecisionOptions = RoutingDecisionOptions.LoadFromEnvironment();
var networkAssessmentOptions = NetworkAssessmentOptions.LoadFromEnvironment();
var survivalOptions = SurvivalOptions.LoadFromEnvironment();
var eventBusOptions = EventBusOptions.LoadFromEnvironment();
var devOpenApiEnabled = string.Equals(Environment.GetEnvironmentVariable("ROUTER_DEV_OPENAPI_ENABLED"), "true", StringComparison.OrdinalIgnoreCase);

builder.Services.AddSingleton(config);
builder.Services.AddSingleton(traceQueryOptions);
builder.Services.AddSingleton(routingDecisionOptions);
builder.Services.AddSingleton(networkAssessmentOptions);
builder.Services.AddSingleton(survivalOptions);
builder.Services.AddSingleton(eventBusOptions);
builder.Services.AddSingleton<EventBusPublisher>();
builder.Services.AddSingleton<EventBusReader>();
builder.Services.AddScoped<JourneyRequestContext>();
builder.Services.AddScoped<RouterJourneyEmitter>();
builder.Services.AddSingleton<TraceSpanStore>();
builder.Services.AddSingleton<RouterMetrics>();
builder.Services.AddControllers();
if (devOpenApiEnabled)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
}
if (traceQueryOptions.Enabled)
{
    builder.Services.AddHostedService<TraceSpanListener>();
}
builder.Services.AddSingleton<SurvivalStateStore>();
builder.Services.AddSingleton<SurvivalStatusClient>();
builder.Services.AddSingleton<ComplexityClassifier>();
builder.Services.AddSingleton<RoutingDecisionEngine>();
builder.Services.AddSingleton<IRoutingDecisionSink, RoutingDecisionLogger>();
builder.Services.AddSingleton<IRoutingDecisionSink, RoutingDecisionTelemetrySink>();
builder.Services.AddSingleton<NetworkAssessmentScorer>();
builder.Services.AddSingleton<MeshAssessmentClient>();
builder.Services.AddSingleton<MeshHealthCache>();
builder.Services.AddSingleton<NetworkAssessmentService>();
builder.Services.AddSingleton<RouteAttestationReplayStore>();
builder.Services.AddSingleton<RouteAttestationValidator>();
builder.Services.AddSingleton<RouteAttestationService>();
if (survivalOptions.Enabled)
{
    builder.Services.AddHostedService<SurvivalPoller>();
}

builder.Services.AddHttpClient("orchestrator", client =>
{
    client.Timeout = TimeSpan.FromSeconds(config.OrchestratorTimeoutSeconds);
    client.BaseAddress = new Uri(config.OrchestratorUrl);
}).ConfigurePrimaryHttpMessageHandler(sp =>
    Program.BuildOrchestratorHandler(
        config,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("Cwi.Router.OrchestratorMTLS")));

builder.Services.AddHttpClient("coordination", client =>
{
    client.Timeout = TimeSpan.FromSeconds(config.OrchestratorTimeoutSeconds);
    var baseUrl = string.IsNullOrWhiteSpace(config.CoordinationLayerUrl)
        ? config.OrchestratorUrl
        : config.CoordinationLayerUrl;
    client.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddHttpClient("model-probe", client =>
{
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddTransient<OrchestratorClient>();
builder.Services.AddTransient<IOrchestratorClient>(sp =>
{
    IOrchestratorClient client = sp.GetRequiredService<OrchestratorClient>();
    client = new OrchestratorClientRetryDecorator(client, maxAttempts: 2, delay: TimeSpan.FromMilliseconds(100));
    client = new OrchestratorClientLoggingDecorator(
        client,
        sp.GetRequiredService<ILogger<OrchestratorClientLoggingDecorator>>());
    return client;
});

builder.Services.AddTransient<CoordinationClient>();
builder.Services.AddTransient<ICoordinationClient>(sp =>
{
    ICoordinationClient client = sp.GetRequiredService<CoordinationClient>();
    client = new CoordinationClientRetryDecorator(client, maxAttempts: 2, delay: TimeSpan.FromMilliseconds(100));
    client = new CoordinationClientLoggingDecorator(
        client,
        sp.GetRequiredService<ILogger<CoordinationClientLoggingDecorator>>());
    return client;
});
builder.Services.AddTransient<DependencyHealthChecker>();
builder.Services.AddSingleton<ModelEndpointCircuitBreaker>();
builder.Services.AddSingleton<ModelEndpointRegistry>();
builder.Services.AddHostedService<ModelEndpointProbeService>();
builder.Services.AddSingleton<OrchestratorCircuitBreaker>();
builder.Services.AddSingleton<CoordinationCircuitBreaker>();

builder.Services.AddTransient<IRouterHandler, RequestValidationHandler>();
builder.Services.AddTransient<IRouterHandler, ActionSchemaValidationHandler>();
builder.Services.AddTransient<IRouterHandler, RouteAttestationValidationHandler>();
builder.Services.AddTransient<IRouterHandler, AuthHandler>();
builder.Services.AddTransient<IRouterHandler, MtlsHandler>();
builder.Services.AddTransient<IRouterHandler, RateLimitHandler>();
builder.Services.AddTransient<IRouterHandler>(sp => new TenantRoutingHandler(sp.GetRequiredService<RouterConfig>()));
builder.Services.AddTransient<IRouterHandler, PolicyModeHandler>();
builder.Services.AddTransient<IRouterHandler, SurvivalPriorityHandler>();
builder.Services.AddTransient<IRouterHandler, RoutingDecisionHandler>();
builder.Services.AddTransient<IRouterHandler, TierSelectionHandler>();
builder.Services.AddTransient<IRouterHandler, ModelHealthCircuitBreakerHandler>();
builder.Services.AddTransient<IRouterHandler, MeshProxyHandler>();
builder.Services.AddTransient<IRouterHandler, ForwardProxyHandler>();

builder.Services.AddScoped<RouterPipeline>();

builder.WebHost.UseUrls($"http://0.0.0.0:{config.HttpPort}");

var app = builder.Build();
AttachEventBus(app, eventBusOptions);
if (devOpenApiEnabled)
{
    app.UseSwagger(options =>
    {
        options.RouteTemplate = "api/dev/openapi/{documentName}.json";
    });
}

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("Cwi.Router.Requests");
    var stopwatch = Stopwatch.StartNew();

    var traceId = EnsureTraceContext(context);
    var correlationId = EnsureCorrelationId(context, traceId);
    context.TraceIdentifier = traceId;
    context.Response.Headers["X-Request-Id"] = correlationId;

    string? userId = null;

    if (context.Request.ContentLength is > 0 &&
        context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
    {
        context.Request.EnableBuffering();
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync().ConfigureAwait(false);
        context.Request.Body.Position = 0;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("user_id", out var userProp) &&
                    userProp.ValueKind == JsonValueKind.String)
                {
                    userId = userProp.GetString();
                }
            }
            catch (JsonException)
            {
            }
        }
    }

    try
    {
        await next().ConfigureAwait(false);
    }
    finally
    {
        stopwatch.Stop();
        var tenantId = context.Items.TryGetValue("tenant_id", out var tenantValue) ? tenantValue as string : null;

        logger.LogInformation(
            "request completed trace_id={TraceId} correlation_id={CorrelationId} tenant_id={TenantId} user_id={UserId} endpoint={Endpoint} method={Method} status={StatusCode} latency_ms={LatencyMs}",
            traceId,
            correlationId,
            string.IsNullOrWhiteSpace(tenantId) ? "unknown" : tenantId,
            string.IsNullOrWhiteSpace(userId) ? "unknown" : userId,
            context.Request.Path.Value ?? string.Empty,
            context.Request.Method,
            context.Response.StatusCode,
            stopwatch.ElapsedMilliseconds);
    }
});

app.MapControllers();
app.Run();

void AttachEventBus(WebApplication app, EventBusOptions options)
{
    if (!options.Enabled)
    {
        return;
    }

    var publisher = app.Services.GetRequiredService<EventBusPublisher>();
    app.Lifetime.ApplicationStarted.Register(() =>
        _ = publisher.PublishAsync(new EventEnvelope
        {
            Type = "service.start",
            Polarity = "positive",
            Severity = "info",
            Message = "Router started."
        }));

    app.Lifetime.ApplicationStopping.Register(() =>
        _ = publisher.PublishAsync(new EventEnvelope
        {
            Type = "service.stop",
            Polarity = "negative",
            Severity = "warn",
            Message = "Router stopping."
        }));

    AppDomain.CurrentDomain.UnhandledException += (_, args) =>
    {
        if (args.ExceptionObject is Exception ex)
        {
            _ = publisher.PublishAsync(new EventEnvelope
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
    public static HttpClientHandler BuildOrchestratorHandler(RouterConfig config, ILogger logger)
    {
        var handler = new HttpClientHandler();

        if (!string.IsNullOrWhiteSpace(config.OrchestratorClientCertPath))
        {
            handler.ClientCertificates.Add(LoadCertificate(
                config.OrchestratorClientCertPath,
                config.OrchestratorClientCertPassword));
        }

        if (!string.IsNullOrWhiteSpace(config.OrchestratorCaPath))
        {
            var caCert = new X509Certificate2(config.OrchestratorCaPath);
            handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
                ValidateServerCertificate(certificate as X509Certificate2, caCert, logger);
        }

        return handler;
    }

    public static X509Certificate2 LoadCertificate(string path, string? password)
    {
        return string.IsNullOrWhiteSpace(password)
            ? new X509Certificate2(path)
            : new X509Certificate2(path, password);
    }

    public static bool ValidateServerCertificate(
        X509Certificate2? certificate,
        X509Certificate2 caCertificate,
        ILogger logger)
    {
        if (certificate is null)
        {
            logger.LogWarning("mTLS validation failed: missing server certificate.");
            return false;
        }

        var revokedSerials = Environment.GetEnvironmentVariable("ORCHESTRATOR_MTLS_REVOKED_SERIALS");
        if (!string.IsNullOrWhiteSpace(revokedSerials))
        {
            var normalizedSerial = NormalizeSerial(certificate.SerialNumber);
            var revoked = revokedSerials.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(serial => NormalizeSerial(serial))
                .Any(serial => string.Equals(serial, normalizedSerial, StringComparison.OrdinalIgnoreCase));
            if (revoked)
            {
                logger.LogWarning(
                    "mTLS validation failed: server certificate serial revoked. serial={Serial}.",
                    certificate.SerialNumber);
                return false;
            }
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(caCertificate);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;

        var valid = chain.Build(certificate);
        if (!valid)
        {
            logger.LogWarning("mTLS validation failed for server certificate subject={Subject}.", certificate.Subject);
        }
        return valid;
    }

    private static string NormalizeSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return string.Empty;
        }

        return serial.Trim().TrimStart('0').ToUpperInvariant();
    }

    private static string EnsureTraceContext(HttpContext context)
    {
        var traceParent = context.Request.Headers["traceparent"].ToString();
        var traceState = context.Request.Headers["tracestate"].ToString();
        if (!string.IsNullOrWhiteSpace(traceParent) &&
            ActivityContext.TryParse(traceParent, traceState, out var activityContext))
        {
            return activityContext.TraceId.ToString();
        }

        var current = Activity.Current;
        if (current is not null &&
            current.IdFormat == ActivityIdFormat.W3C &&
            !string.IsNullOrWhiteSpace(current.Id))
        {
            context.Request.Headers["traceparent"] = current.Id;
            if (!string.IsNullOrWhiteSpace(current.TraceStateString))
            {
                context.Request.Headers["tracestate"] = current.TraceStateString;
            }
            return current.TraceId.ToString();
        }

        var generatedTraceId = ActivityTraceId.CreateRandom();
        var generatedSpanId = ActivitySpanId.CreateRandom();
        var generatedTraceParent = $"00-{generatedTraceId}-{generatedSpanId}-01";
        context.Request.Headers["traceparent"] = generatedTraceParent;
        return generatedTraceId.ToString();
    }

    private static string EnsureCorrelationId(HttpContext context, string traceId)
    {
        var requestId = context.Request.Headers["X-Request-Id"].ToString();
        if (!string.IsNullOrWhiteSpace(requestId))
        {
            return requestId;
        }

        context.Request.Headers["X-Request-Id"] = traceId;
        return traceId;
    }
}
