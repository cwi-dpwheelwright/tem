using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassIssueResponseDto
{
    [JsonPropertyName("token_id")]
    public string TokenId { get; init; } = string.Empty;

    [JsonPropertyName("expires_utc")]
    public DateTimeOffset ExpiresUtc { get; init; }
}
