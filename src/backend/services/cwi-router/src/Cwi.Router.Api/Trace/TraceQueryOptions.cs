namespace Cwi.Router.Api.Trace;

public sealed class TraceQueryOptions
{
    public bool Enabled { get; init; }
    public int MaxTraces { get; init; } = 200;
    public int MaxSpansPerTrace { get; init; } = 200;
    public int MaxSpanAgeSeconds { get; init; } = 600;
    public string? AuthToken { get; init; }

    public static TraceQueryOptions LoadFromEnvironment()
    {
        return new TraceQueryOptions
        {
            Enabled = ParseBool("ROUTER_TRACE_QUERY_ENABLED", false),
            MaxTraces = ParseInt("ROUTER_TRACE_QUERY_MAX_TRACES", 200),
            MaxSpansPerTrace = ParseInt("ROUTER_TRACE_QUERY_MAX_SPANS_PER_TRACE", 200),
            MaxSpanAgeSeconds = ParseInt("ROUTER_TRACE_QUERY_MAX_SPAN_AGE_SECONDS", 600),
            AuthToken = Environment.GetEnvironmentVariable("ROUTER_TRACE_QUERY_TOKEN")
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
