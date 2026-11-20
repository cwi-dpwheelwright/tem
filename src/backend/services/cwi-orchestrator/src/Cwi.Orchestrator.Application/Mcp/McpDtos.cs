#nullable enable
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Mcp;

public sealed class McpToolRegistryDocument
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("catalog_name")]
    public string CatalogName { get; init; } = string.Empty;

    [JsonPropertyName("tools")]
    public IReadOnlyList<McpToolDefinition> Tools { get; init; } = Array.Empty<McpToolDefinition>();
}

public sealed class McpToolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("risk_class")]
    public string? RiskClass { get; init; }

    [JsonPropertyName("method")]
    public string Method { get; init; } = "POST";

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    [JsonPropertyName("connector_id")]
    public string? ConnectorId { get; init; }

    [JsonPropertyName("requires_approval")]
    public bool RequiresApproval { get; init; }

    [JsonPropertyName("log_to_ledger")]
    public bool LogToLedger { get; init; } = true;
}

public sealed class McpPolicyDocument
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("policy_name")]
    public string PolicyName { get; init; } = string.Empty;

    [JsonPropertyName("rules")]
    public IReadOnlyList<McpPolicyRule> Rules { get; init; } = Array.Empty<McpPolicyRule>();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed class McpPolicyRule
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("enforce")]
    public bool Enforce { get; init; }

    [JsonPropertyName("categories")]
    public Dictionary<string, bool>? Categories { get; init; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<string>? Tools { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public sealed record McpPolicyEvaluation(
    bool Blocked,
    bool RequiresApproval,
    bool RequiresLedger,
    string Reason);

public sealed record McpExecutionResult(
    bool Executed,
    Dictionary<string, object?> Result);
#nullable disable
