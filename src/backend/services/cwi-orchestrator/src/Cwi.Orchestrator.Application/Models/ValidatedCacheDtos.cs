#nullable enable
using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class ValidatedCacheEntryDto
{
    [JsonPropertyName("cache_key")]
    public string CacheKey { get; init; } = string.Empty;

    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; init; } = "tenant";

    [JsonPropertyName("user_id_hash")]
    public string? UserIdHash { get; init; }

    [JsonPropertyName("source_proof_json")]
    public string SourceProofJson { get; init; } = "{}";

    [JsonPropertyName("trace_id")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("fresh_until_utc")]
    public DateTimeOffset FreshUntilUtc { get; init; }

    [JsonPropertyName("policy_mode")]
    public string PolicyMode { get; init; } = string.Empty;

    [JsonPropertyName("policy_version")]
    public string? PolicyVersion { get; init; }

    [JsonPropertyName("pack_hash")]
    public string PackHash { get; init; } = string.Empty;

    [JsonPropertyName("validation_status")]
    public string ValidationStatus { get; init; } = "pending";

    [JsonPropertyName("critic_status")]
    public string? CriticStatus { get; init; }

    [JsonPropertyName("payload_hash")]
    public string? PayloadHash { get; init; }
}

public sealed class ValidatedCacheLookupContextDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("user_id_hash")]
    public string? UserIdHash { get; init; }

    [JsonPropertyName("policy_mode")]
    public string PolicyMode { get; init; } = string.Empty;

    [JsonPropertyName("policy_version")]
    public string? PolicyVersion { get; init; }

    [JsonPropertyName("pack_hash")]
    public string PackHash { get; init; } = string.Empty;

    [JsonPropertyName("now_utc")]
    public DateTimeOffset NowUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ValidatedCacheEvaluationDto
{
    [JsonPropertyName("cache_key")]
    public string CacheKey { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = ValidatedCacheStates.Invalid;

    [JsonPropertyName("reasons")]
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    [JsonPropertyName("fresh_until_utc")]
    public DateTimeOffset? FreshUntilUtc { get; init; }
}

public static class ValidatedCacheStates
{
    public const string Eligible = "eligible";
    public const string Stale = "stale";
    public const string Invalid = "invalid";
    public const string RequiresRefresh = "requires_refresh";
}
#nullable disable
