using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassRevokeResponseDto
{
    [JsonPropertyName("token_id")]
    public string TokenId { get; init; } = string.Empty;

    [JsonPropertyName("revoked_utc")]
    public DateTimeOffset RevokedUtc { get; init; }
}
