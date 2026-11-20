using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassIssueRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; init; } = string.Empty;

    [JsonPropertyName("duration_minutes")]
    public int DurationMinutes { get; init; }

    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}
