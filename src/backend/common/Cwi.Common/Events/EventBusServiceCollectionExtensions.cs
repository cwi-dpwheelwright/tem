using Microsoft.Extensions.DependencyInjection;

namespace Cwi.Common.Events;

public static class EventBusServiceCollectionExtensions
{
    public static IServiceCollection AddEventBusClient(this IServiceCollection services)
    {
        var options = EventBusClientOptions.FromEnvironment();
        services.AddSingleton(options);
        services.AddHttpClient<EventBusClient>();
        return services;
    }
}
