#nullable enable
namespace Cwi.Orchestrator.Application.Mcp;

public sealed record McpConnectorDefinition(
    string Id,
    string Type,
    bool Enabled);

public sealed record McpConnectorLookup(
    bool ConfigAvailable,
    McpConnectorDefinition? Connector,
    string Reason);

public sealed class McpConnectorCatalog
{
    private readonly McpExecutionOptions _options;

    public McpConnectorCatalog(McpExecutionOptions options)
    {
        _options = options;
    }

    public McpConnectorLookup FindById(string? connectorId)
    {
        if (string.IsNullOrWhiteSpace(connectorId))
        {
            return new McpConnectorLookup(
                ConfigAvailable: false,
                Connector: null,
                Reason: "MCP tool is not mapped to a connector_id.");
        }

        if (string.IsNullOrWhiteSpace(_options.ConnectorsConfigPath))
        {
            return new McpConnectorLookup(
                ConfigAvailable: false,
                Connector: null,
                Reason: "CONNECTORS_CONFIG_PATH is not configured.");
        }

        if (!File.Exists(_options.ConnectorsConfigPath))
        {
            return new McpConnectorLookup(
                ConfigAvailable: false,
                Connector: null,
                Reason: $"Connector config '{_options.ConnectorsConfigPath}' was not found.");
        }

        var connectors = ParseConnectors(_options.ConnectorsConfigPath);
        var connector = connectors.FirstOrDefault(item =>
            string.Equals(item.Id, connectorId.Trim(), StringComparison.OrdinalIgnoreCase));

        return connector is null
            ? new McpConnectorLookup(
                ConfigAvailable: true,
                Connector: null,
                Reason: $"Connector '{connectorId}' is not defined in connectors.yaml.")
            : new McpConnectorLookup(
                ConfigAvailable: true,
                Connector: connector,
                Reason: connector.Enabled
                    ? $"Connector '{connector.Id}' is enabled."
                    : $"Connector '{connector.Id}' is disabled.");
    }

    private static IReadOnlyList<McpConnectorDefinition> ParseConnectors(string path)
    {
        var connectors = new List<McpConnectorDefinition>();
        Dictionary<string, string>? current = null;
        var inConnectors = false;

        foreach (var rawLine in File.ReadLines(path))
        {
            var leadingSpaces = rawLine.TakeWhile(char.IsWhiteSpace).Count();
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var trimmed = line.TrimStart();
            if (!inConnectors)
            {
                if (string.Equals(trimmed, "connectors:", StringComparison.OrdinalIgnoreCase))
                {
                    inConnectors = true;
                }

                continue;
            }

            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                AddConnector(connectors, current);
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                AddKeyValue(current, trimmed[2..]);
                continue;
            }

            if (current is not null)
            {
                if (leadingSpaces <= 4)
                {
                    AddKeyValue(current, trimmed);
                }
            }
        }

        AddConnector(connectors, current);
        return connectors;
    }

    private static void AddConnector(
        ICollection<McpConnectorDefinition> connectors,
        IReadOnlyDictionary<string, string>? values)
    {
        if (values is null)
        {
            return;
        }

        var id = values.TryGetValue("id", out var idValue) ? idValue : null;
        if (string.IsNullOrWhiteSpace(id) && values.TryGetValue("connector_id", out var connectorIdValue))
        {
            id = connectorIdValue;
        }

        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var type = values.TryGetValue("type", out var typeValue) ? typeValue : string.Empty;
        var enabled = !values.TryGetValue("enabled", out var enabledValue) || ParseBool(enabledValue, defaultValue: true);
        connectors.Add(new McpConnectorDefinition(id.Trim(), type.Trim(), enabled));
    }

    private static void AddKeyValue(IDictionary<string, string> values, string text)
    {
        var index = text.IndexOf(':');
        if (index <= 0)
        {
            return;
        }

        var key = text[..index].Trim();
        var value = CleanScalar(text[(index + 1)..]);
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
        {
            values[key] = value;
        }
    }

    private static string CleanScalar(string value)
    {
        var trimmed = value.Trim();
        var commentIndex = trimmed.IndexOf('#');
        if (commentIndex >= 0)
        {
            trimmed = trimmed[..commentIndex].Trim();
        }

        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed;
    }

    private static bool ParseBool(string value, bool defaultValue)
    {
        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "1" or "yes" or "y" or "on" => true,
            "0" or "no" or "n" or "off" => false,
            _ => defaultValue
        };
    }
}
#nullable disable
