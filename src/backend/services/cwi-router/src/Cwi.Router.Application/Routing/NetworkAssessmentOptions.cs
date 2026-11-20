namespace Cwi.Router.Application.Routing;

public sealed class NetworkAssessmentOptions
{
    public bool Enabled { get; init; } = true;
    public bool MeshHealthEnabled { get; init; } = true;
    public bool HintEnabled { get; init; } = true;
    public double ScoreWeight { get; init; } = 0.2;
    public double HealthyThreshold { get; init; } = 0.7;
    public double DegradedThreshold { get; init; } = 0.4;
    public bool TriggerRerouteOnDegraded { get; init; } = true;
    public int MeshCacheSeconds { get; init; } = 10;
    public int MeshTimeoutSeconds { get; init; } = 2;
    public int MeshHintTimeoutSeconds { get; init; } = 2;

    public static NetworkAssessmentOptions LoadFromEnvironment()
    {
        return new NetworkAssessmentOptions
        {
            Enabled = ParseBool("ROUTER_NETWORK_ASSESSMENT_ENABLED", true),
            MeshHealthEnabled = ParseBool("ROUTER_MESH_HEALTH_ENABLED", true),
            HintEnabled = ParseBool("ROUTER_MESH_HINT_ENABLED", true),
            ScoreWeight = ParseDouble("ROUTER_NETWORK_SCORE_WEIGHT", 0.2),
            HealthyThreshold = ParseDouble("ROUTER_NETWORK_HEALTHY_THRESHOLD", 0.7),
            DegradedThreshold = ParseDouble("ROUTER_NETWORK_DEGRADED_THRESHOLD", 0.4),
            TriggerRerouteOnDegraded = ParseBool("ROUTER_NETWORK_REROUTE_ON_DEGRADED", true),
            MeshCacheSeconds = ParseInt("ROUTER_MESH_HEALTH_CACHE_SECONDS", 10),
            MeshTimeoutSeconds = ParseInt("ROUTER_MESH_HEALTH_TIMEOUT_SECONDS", 2),
            MeshHintTimeoutSeconds = ParseInt("ROUTER_MESH_HINT_TIMEOUT_SECONDS", 2)
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

    private static double ParseDouble(string name, double defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        if (double.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return defaultValue;
    }
}
