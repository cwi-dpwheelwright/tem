namespace Cwi.Orchestrator.Domain.Entities;

public sealed class LedgerOutboxEntry
{
    public Guid Id { get; set; }
    public string TraceId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public string PrevHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string HashAlgorithm { get; set; } = LedgerHasher.DefaultSuite;
    public string? Signature { get; set; }
    public string? SignatureAlgorithm { get; set; }
    public string? KeyId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; }
    public DateTimeOffset? PublishedUtc { get; set; }
}
