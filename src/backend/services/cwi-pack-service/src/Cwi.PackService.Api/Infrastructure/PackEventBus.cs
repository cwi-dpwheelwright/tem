using Cwi.Common.Events;

namespace Cwi.PackService.Api.Infrastructure;

public static class PackEventBus
{
    public static void AttachEventBus(WebApplication app)
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
                Message = "Pack service started."
            }));

        app.Lifetime.ApplicationStopping.Register(() =>
            _ = client.PublishAsync(new EventEnvelope
            {
                Type = "service.stop",
                Polarity = "negative",
                Severity = "warn",
                Message = "Pack service stopping."
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
}
