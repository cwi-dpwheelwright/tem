namespace Cwi.Router.Application.Routing;

public sealed class RoutingDecisionOptions
{
    public bool Enabled { get; init; } = true;
    public bool OverrideTierWhenMissing { get; init; } = true;
    public bool EmitTelemetry { get; init; }
    public string? TelemetryTenantFallback { get; init; }

    public static RoutingDecisionOptions LoadFromEnvironment()
    {
        return new RoutingDecisionOptions
        {
            Enabled = ParseBool("ROUTER_ROUTING_DECISION_ENABLED", true),
            OverrideTierWhenMissing = ParseBool("ROUTER_ROUTING_DECISION_OVERRIDE_TIER", true),
            EmitTelemetry = ParseBool("ROUTER_ROUTING_TELEMETRY_ENABLED", false),
            TelemetryTenantFallback = Environment.GetEnvironmentVariable("ROUTER_ROUTING_TELEMETRY_TENANT")
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
}
