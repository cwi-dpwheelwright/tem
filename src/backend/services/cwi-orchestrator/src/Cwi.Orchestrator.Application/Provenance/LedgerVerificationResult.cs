namespace Cwi.Orchestrator.Application.Provenance;

public sealed class LedgerVerificationResult
{
    public bool IsValid { get; init; }
    public string? Error { get; init; }
}
