#nullable enable
namespace Cwi.Orchestrator.Application.Mcp;

public sealed class McpExecutionOptions
{
    public bool Enabled { get; init; }
    public string ExecutionMode { get; init; } = "read_only";
    public string? ToolRegistryUrl { get; init; }
    public string? PolicyRulesUrl { get; init; }
    public string? ConnectorsConfigPath { get; init; }
    public IReadOnlySet<string> ReadOnlyAllowedTools { get; init; } = DefaultReadOnlyAllowedTools();
    public int HttpTimeoutSeconds { get; init; } = 10;

    public bool IsDisabled =>
        !Enabled || string.Equals(NormalizedExecutionMode, "disabled", StringComparison.OrdinalIgnoreCase);

    public bool IsReadOnly =>
        string.Equals(NormalizedExecutionMode, "read_only", StringComparison.OrdinalIgnoreCase);

    public bool IsApprovalRequired =>
        string.Equals(NormalizedExecutionMode, "approval_required", StringComparison.OrdinalIgnoreCase);

    public bool IsFull =>
        string.Equals(NormalizedExecutionMode, "full", StringComparison.OrdinalIgnoreCase);

    private string NormalizedExecutionMode =>
        string.IsNullOrWhiteSpace(ExecutionMode) ? "disabled" : ExecutionMode.Trim().ToLowerInvariant();

    public static McpExecutionOptions LoadFromEnvironment()
    {
        var enabled = ParseBool("MCP_EXECUTION_ENABLED", defaultValue: false);
        return new McpExecutionOptions
        {
            Enabled = enabled,
            ExecutionMode = ResolveExecutionMode(enabled),
            ToolRegistryUrl = Normalize(Environment.GetEnvironmentVariable("MCP_TOOL_REGISTRY_URL")),
            PolicyRulesUrl = Normalize(Environment.GetEnvironmentVariable("MCP_POLICY_RULES_URL")),
            ConnectorsConfigPath = Normalize(Environment.GetEnvironmentVariable("CONNECTORS_CONFIG_PATH")),
            ReadOnlyAllowedTools = ParseToolSet(
                Environment.GetEnvironmentVariable("MCP_READ_ONLY_ALLOWED_TOOLS"),
                DefaultReadOnlyAllowedTools()),
            HttpTimeoutSeconds = ParseInt("MCP_HTTP_TIMEOUT_SECONDS", defaultValue: 10)
        };
    }

    public bool IsReadOnlyToolAllowed(string? toolName)
    {
        return !string.IsNullOrWhiteSpace(toolName)
               && ReadOnlyAllowedTools.Contains(toolName.Trim());
    }

    private static string ResolveExecutionMode(bool enabled)
    {
        var mode = Normalize(Environment.GetEnvironmentVariable("MCP_EXECUTION_MODE"));
        if (!string.IsNullOrWhiteSpace(mode))
        {
            return IsKnownMode(mode) ? mode : "disabled";
        }

        return enabled ? "read_only" : "disabled";
    }

    private static bool IsKnownMode(string mode)
    {
        return string.Equals(mode, "disabled", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mode, "read_only", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mode, "approval_required", StringComparison.OrdinalIgnoreCase)
               || string.Equals(mode, "full", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlySet<string> ParseToolSet(string? raw, IReadOnlySet<string> fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return fallback;
        }

        var values = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return values.Count == 0 ? fallback : values;
    }

    private static IReadOnlySet<string> DefaultReadOnlyAllowedTools()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "system.status",
            "node.health.check",
            "ledger.view",
            "pack.list",
            "pack.get",
            "pin.resolve",
            "weather.get",
            "search.web"
        };
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ParseBool(string name, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(raw) ? defaultValue : bool.TryParse(raw, out var value) ? value : defaultValue;
    }

    private static int ParseInt(string name, int defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        return int.TryParse(raw, out var value) && value > 0 ? value : defaultValue;
    }
}
#nullable disable
