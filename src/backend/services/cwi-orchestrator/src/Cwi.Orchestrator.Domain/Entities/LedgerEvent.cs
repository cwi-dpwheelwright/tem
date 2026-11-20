using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Domain.Entities;

public sealed class LedgerEvent
{
    [JsonPropertyName("id")]
    public Guid Id { get; set; }

    [JsonPropertyName("trace_id")]
    public string TraceId { get; set; } = string.Empty;

    [JsonPropertyName("event_type")]
    public string EventType { get; set; } = string.Empty;

    [JsonPropertyName("payload_json")]
    public string PayloadJson { get; set; } = string.Empty;

    [JsonPropertyName("prev_hash")]
    public string PrevHash { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("hash_algorithm")]
    public string HashAlgorithm { get; set; } = LedgerHasher.DefaultSuite;

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    [JsonPropertyName("signature_algorithm")]
    public string? SignatureAlgorithm { get; set; }

    [JsonPropertyName("key_id")]
    public string? KeyId { get; set; }

    [JsonPropertyName("created_utc")]
    public DateTimeOffset CreatedUtc { get; set; }

    public static LedgerEvent Create(
        string traceId,
        string eventType,
        string payloadJson,
        string prevHash,
        string? hashAlgorithm = null)
    {
        var createdUtc = DateTimeOffset.UtcNow;
        var resolvedAlgorithm = LedgerHasher.ResolveSuite(hashAlgorithm);
        var hash = LedgerHasher.ComputeHash(prevHash, traceId, eventType, payloadJson, createdUtc, resolvedAlgorithm);

        return new LedgerEvent
        {
            Id = Guid.NewGuid(),
            TraceId = traceId,
            EventType = eventType,
            PayloadJson = payloadJson,
            PrevHash = prevHash,
            Hash = hash,
            HashAlgorithm = resolvedAlgorithm,
            CreatedUtc = createdUtc
        };
    }
}
