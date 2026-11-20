namespace Cwi.Router.Application.Survival;

public sealed class SurvivalOptions
{
    public bool Enabled { get; init; } = true;
    public int PollSeconds { get; init; } = 15;
    public bool ReadinessFailsOnSurvival { get; init; }

    public static SurvivalOptions LoadFromEnvironment()
    {
        return new SurvivalOptions
        {
            Enabled = ParseBool("ROUTER_SURVIVAL_ENABLED", true),
            PollSeconds = ParseInt("ROUTER_SURVIVAL_POLL_SECONDS", 15),
            ReadinessFailsOnSurvival = ParseBool("ROUTER_SURVIVAL_READY_FAIL", false)
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
