using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassRevokeRequestDto
{
    [JsonPropertyName("token_id")]
    public string TokenId { get; init; } = string.Empty;
}
