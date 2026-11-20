using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class AskRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("question")]
    public string Question { get; init; } = string.Empty;

    [JsonPropertyName("pack")]
    public PackRef Pack { get; init; } = new();

    [JsonPropertyName("context")]
    public AskContext Context { get; init; } = new();
}

public sealed class PackRef
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("anchor")]
    public string? Anchor { get; init; }
}

public sealed class AskContext
{
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = string.Empty;

    [JsonPropertyName("top_k")]
    public int TopK { get; init; } = 8;

    [JsonPropertyName("use_web")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UseWeb { get; init; }

    [JsonPropertyName("trusted_web_requested")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TrustedWebRequested { get; init; }

    [JsonPropertyName("conversation_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConversationId { get; init; }

    [JsonPropertyName("timezone")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Timezone { get; init; }

    [JsonPropertyName("web_retrieval")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AskWebRetrievalContext? WebRetrieval { get; init; }
}

public sealed class AskWebRetrievalContext
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("mode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Mode { get; init; }

    [JsonPropertyName("profile")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Profile { get; init; }

    [JsonPropertyName("max_results")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxResults { get; init; }
}
