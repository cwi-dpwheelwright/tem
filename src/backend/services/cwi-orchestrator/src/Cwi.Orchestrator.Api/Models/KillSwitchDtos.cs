#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Api.Models;

public sealed class KillSwitchStatusResponse
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("irreversible")]
    public bool Irreversible { get; init; }

    [JsonPropertyName("updated_utc")]
    public string? UpdatedUtc { get; init; }
}

public sealed class KillSwitchToggleRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; }

    [JsonPropertyName("kill_code")]
    public string? KillCode { get; init; }

    [JsonPropertyName("token_id")]
    public string? TokenId { get; init; }

    [JsonPropertyName("irreversible")]
    public bool Irreversible { get; init; }
}

public sealed class KillSwitchTokenRequest
{
    [JsonPropertyName("kill_code")]
    public string? KillCode { get; init; }

    [JsonPropertyName("ttl_seconds")]
    public int? TtlSeconds { get; init; }
}

public sealed class KillSwitchTokenResponse
{
    [JsonPropertyName("token_id")]
    public string TokenId { get; init; } = string.Empty;

    [JsonPropertyName("expires_utc")]
    public string ExpiresUtc { get; init; } = string.Empty;
}
#nullable disable
