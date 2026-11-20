using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Application.Provenance;

public sealed class LedgerEventPage
{
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Total { get; init; }
    public IReadOnlyList<LedgerEvent> Items { get; init; } = Array.Empty<LedgerEvent>();
}
