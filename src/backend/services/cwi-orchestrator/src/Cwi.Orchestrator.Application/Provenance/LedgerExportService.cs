using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Provenance;

public sealed class LedgerExportService
{
    private readonly ILedgerRepository _ledgerRepository;

    public LedgerExportService(ILedgerRepository ledgerRepository)
    {
        _ledgerRepository = ledgerRepository;
    }

    public async Task<LedgerExportPage> ExportAsync(
        string traceId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var safePage = page < 1 ? 1 : page;
        var safeSize = pageSize switch
        {
            < 1 => 50,
            > 500 => 500,
            _ => pageSize
        };

        var total = await _ledgerRepository.GetCountAsync(traceId, cancellationToken).ConfigureAwait(false);
        var skip = (safePage - 1) * safeSize;
        var items = await _ledgerRepository
            .GetByTraceIdAsync(traceId, skip, safeSize, cancellationToken)
            .ConfigureAwait(false);

        return new LedgerExportPage
        {
            Page = safePage,
            PageSize = safeSize,
            Total = total,
            Items = items
        };
    }
}
