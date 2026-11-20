namespace Cwi.Router.Application.EventBus;

public sealed record EventBusOptions
{
    public bool Enabled { get; init; }
    public string RedisUrl { get; init; } = string.Empty;
    public string Stream { get; init; } = "cwi.events";
    public int RetentionDays { get; init; } = 7;
    public string? ReadToken { get; init; }
    public string? WriteToken { get; init; }

    public static EventBusOptions LoadFromEnvironment()
    {
        var enabledRaw = Environment.GetEnvironmentVariable("EVENT_BUS_ENABLED");
        var enabled = string.Equals(enabledRaw, "true", StringComparison.OrdinalIgnoreCase);

        var redisUrl = Environment.GetEnvironmentVariable("EVENT_BUS_REDIS_URL")
            ?? Environment.GetEnvironmentVariable("REDIS_URL")
            ?? string.Empty;

        var stream = Environment.GetEnvironmentVariable("EVENT_BUS_STREAM");
        var retentionRaw = Environment.GetEnvironmentVariable("EVENT_BUS_RETENTION_DAYS");
        var retention = 7;
        if (!string.IsNullOrWhiteSpace(retentionRaw) && int.TryParse(retentionRaw, out var parsed))
        {
            retention = Math.Max(parsed, 0);
        }

        return new EventBusOptions
        {
            Enabled = enabled,
            RedisUrl = NormalizeRedisUrl(redisUrl),
            Stream = string.IsNullOrWhiteSpace(stream) ? "cwi.events" : stream,
            RetentionDays = retention,
            ReadToken = Environment.GetEnvironmentVariable("EVENT_BUS_READ_TOKEN"),
            WriteToken = Environment.GetEnvironmentVariable("EVENT_BUS_WRITE_TOKEN")
        };
    }

    private static string NormalizeRedisUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value;
        }

        if (!string.Equals(uri.Scheme, "redis", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, "rediss", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        var credentials = string.IsNullOrWhiteSpace(uri.UserInfo) ? string.Empty : $"{uri.UserInfo}@";
        var host = uri.Host;
        var port = uri.IsDefaultPort
            ? (string.Equals(uri.Scheme, "rediss", StringComparison.OrdinalIgnoreCase) ? 6380 : 6379)
            : uri.Port;

        return $"{credentials}{host}:{port}";
    }
}
