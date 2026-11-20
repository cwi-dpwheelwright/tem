namespace Cwi.Orchestrator.Application.Provenance;

public sealed class LedgerHashOptions
{
    public string SuiteId { get; init; } = Cwi.Orchestrator.Domain.LedgerHasher.DefaultSuite;
}
