using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Cwi.Coordination.Api.Health;
using Cwi.Coordination.Api.Realtime;
using Cwi.Coordination.Api.Security;
using Cwi.Coordination.Composition;
using Cwi.Coordination.Application.Transport;
using Cwi.Coordination.Application.Services.Decorators;
using Cwi.Common.Events;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddGrpc();
builder.Services.AddEventBusClient();

builder.Services.AddCoordinationLayer(builder.Configuration);
builder.Services.AddSingleton<IMetricsRecorder, PrometheusMetricsRecorder>();
builder.Services.AddSingleton<DependencyHealthChecker>();
builder.Services.AddSingleton<NodeSessionManager>();
builder.Services.AddSingleton<INodeSessionDispatcher>(sp => sp.GetRequiredService<NodeSessionManager>());
builder.Services.AddScoped<NodeControlWebSocketHandler>();

var adminOptions = builder.Configuration.GetSection("Coordination:Admin")
    .Get<AdminAuthorizationOptions>() ?? new AdminAuthorizationOptions();
builder.Services.AddSingleton(adminOptions);
builder.Services.AddSingleton<AdminAuthorizationService>();

builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("cwi-coordination-layer"))
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracerProviderBuilder.AddOtlpExporter();
        }
    });

var transportOptions = builder.Configuration.GetSection("Transport").Get<TransportOptions>() ?? new TransportOptions();
if (transportOptions.RequireClientCertificate)
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ConfigureHttpsDefaults(httpsOptions =>
        {
            httpsOptions.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
            httpsOptions.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            httpsOptions.ClientCertificateValidation = (cert, _, _) =>
                CertificateValidator.Validate(cert, transportOptions.TrustBundlePath);
        });
    });
}

var app = builder.Build();
AttachEventBus(app);

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseHttpMetrics();
app.UseWebSockets();

app.UseMiddleware<ProtocolVersionMiddleware>();

app.Map("/control/nodes/connect", async context =>
{
    var handler = context.RequestServices.GetRequiredService<NodeControlWebSocketHandler>();
    await handler.HandleAsync(context).ConfigureAwait(false);
});
app.MapControllers();
app.MapGrpcService<Cwi.Coordination.Api.Grpc.CoordinatorGrpcService>();
app.MapMetrics();

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
            Message = "Coordination layer started."
        }));

    app.Lifetime.ApplicationStopping.Register(() =>
        _ = client.PublishAsync(new EventEnvelope
        {
            Type = "service.stop",
            Polarity = "negative",
            Severity = "warn",
            Message = "Coordination layer stopping."
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
}
