using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.BreakGlass;

public sealed class BreakGlassAuditPage
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public IReadOnlyList<BreakGlassAuditTokenDto> Items { get; init; } = Array.Empty<BreakGlassAuditTokenDto>();
}

public sealed class BreakGlassAuditTokenDto
{
    [JsonPropertyName("token_id")]
    public string TokenId { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; init; } = string.Empty;

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("expires_utc")]
    public DateTimeOffset ExpiresUtc { get; init; }

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; init; }

    [JsonPropertyName("revoked_utc")]
    public DateTimeOffset? RevokedUtc { get; init; }
}
