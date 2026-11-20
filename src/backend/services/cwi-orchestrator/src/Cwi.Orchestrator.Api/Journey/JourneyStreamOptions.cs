namespace Cwi.Orchestrator.Api.Journey;

public sealed record JourneyStreamOptions
{
    public bool Enabled { get; init; }
    public string RedisUrl { get; init; } = string.Empty;
    public string Stream { get; init; } = "cwi.events";
    public int BlockMs { get; init; } = 5000;
    public int BatchSize { get; init; } = 100;

    public static JourneyStreamOptions LoadFromEnvironment()
    {
        var enabledRaw = Environment.GetEnvironmentVariable("JOURNEY_ENABLED")
            ?? Environment.GetEnvironmentVariable("EVENT_BUS_ENABLED");
        var enabled = string.Equals(enabledRaw, "true", StringComparison.OrdinalIgnoreCase);

        var redisUrl = Environment.GetEnvironmentVariable("JOURNEY_REDIS_URL")
            ?? Environment.GetEnvironmentVariable("EVENT_BUS_REDIS_URL")
            ?? Environment.GetEnvironmentVariable("REDIS_URL")
            ?? string.Empty;

        var stream = Environment.GetEnvironmentVariable("JOURNEY_STREAM")
            ?? Environment.GetEnvironmentVariable("EVENT_BUS_STREAM")
            ?? "cwi.events";

        var blockMs = ParseInt("JOURNEY_BLOCK_MS", 5000);
        var batchSize = ParseInt("JOURNEY_BATCH_SIZE", 100);

        return new JourneyStreamOptions
        {
            Enabled = enabled,
            RedisUrl = NormalizeRedisUrl(redisUrl),
            Stream = string.IsNullOrWhiteSpace(stream) ? "cwi.events" : stream,
            BlockMs = Math.Clamp(blockMs, 1000, 60000),
            BatchSize = Math.Clamp(batchSize, 1, 500)
        };
    }

    private static int ParseInt(string name, int fallback)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var parsed)
            ? parsed
            : fallback;
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
