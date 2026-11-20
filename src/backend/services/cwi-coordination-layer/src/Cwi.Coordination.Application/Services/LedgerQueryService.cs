using Cwi.Coordination.Domain.Repositories;
using Cwi.Coordination.Application.Models;

namespace Cwi.Coordination.Application.Services;

public sealed class LedgerQueryService
{
    private readonly ILedgerRepository _repository;

    public LedgerQueryService(ILedgerRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<LedgerEventResponse>> ListAsync(int limit, CancellationToken cancellationToken)
    {
        var events = await _repository.ListAsync(limit, cancellationToken).ConfigureAwait(false);
        return events.Select(evt => new LedgerEventResponse
        {
            EventId = evt.Id,
            EventType = evt.EventType,
            PayloadJson = evt.PayloadJson,
            PrevHash = evt.PrevHash,
            Hash = evt.Hash,
            CreatedUtc = evt.CreatedUtc
        }).ToList();
    }
}
