namespace Cwi.Orchestrator.Api.Trace;

public sealed class TraceQueryOptions
{
    public bool Enabled { get; init; }
    public int MaxTraces { get; init; } = 200;
    public int MaxSpansPerTrace { get; init; } = 200;
    public int MaxSpanAgeSeconds { get; init; } = 600;

    public static TraceQueryOptions LoadFromEnvironment()
    {
        return new TraceQueryOptions
        {
            Enabled = ParseBool("CWI_TRACE_QUERY_ENABLED", false),
            MaxTraces = ParseInt("CWI_TRACE_QUERY_MAX_TRACES", 200),
            MaxSpansPerTrace = ParseInt("CWI_TRACE_QUERY_MAX_SPANS_PER_TRACE", 200),
            MaxSpanAgeSeconds = ParseInt("CWI_TRACE_QUERY_MAX_SPAN_AGE_SECONDS", 600)
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
