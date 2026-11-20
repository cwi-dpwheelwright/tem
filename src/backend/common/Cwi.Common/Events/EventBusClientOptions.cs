namespace Cwi.Common.Events;

public sealed record EventBusClientOptions
{
    public bool Enabled { get; init; }
    public string Url { get; init; } = string.Empty;
    public string ServiceName { get; init; } = "unknown";
    public string Environment { get; init; } = "unknown";
    public string? Token { get; init; }

    public static EventBusClientOptions FromEnvironment()
    {
        var enabledRaw = System.Environment.GetEnvironmentVariable("EVENT_BUS_ENABLED");
        var enabled = string.Equals(enabledRaw, "true", StringComparison.OrdinalIgnoreCase);

        return new EventBusClientOptions
        {
            Enabled = enabled,
            Url = System.Environment.GetEnvironmentVariable("EVENT_BUS_URL") ?? string.Empty,
            ServiceName = System.Environment.GetEnvironmentVariable("EVENT_BUS_SERVICE_NAME") ?? "unknown",
            Environment = System.Environment.GetEnvironmentVariable("EVENT_BUS_ENV") ?? "unknown",
            Token = System.Environment.GetEnvironmentVariable("EVENT_BUS_TOKEN")
        };
    }
}
