using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class RoundService
{
    private readonly IRoundRepository _repository;
    private readonly LedgerService _ledger;

    public RoundService(IRoundRepository repository, LedgerService ledger)
    {
        _repository = repository;
        _ledger = ledger;
    }

    public async Task<ServiceResponse<RoundResponse>> CreateAsync(
        RoundCreateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "name is required."), 400);
            }

            var round = new Round
            {
                Name = request.Name,
                Status = RoundStatus.Active
            };
            await _repository.AddAsync(round, cancellationToken).ConfigureAwait(false);
            await _ledger.AppendAsync("round_create", new { round.Id, round.Name }, cancellationToken).ConfigureAwait(false);

            return ServiceResponse<RoundResponse>.Success(new RoundResponse
            {
                RoundId = round.Id,
                Name = round.Name,
                Status = round.Status.ToString().ToLowerInvariant()
            });
        }
        catch (DomainException exc)
        {
            return ServiceResponse<RoundResponse>.Fail(exc.Error, exc.StatusCode, exc.Message);
        }
    }

    public async Task<ServiceResponse<IReadOnlyList<RoundResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        var rounds = await _repository.ListAsync(cancellationToken).ConfigureAwait(false);
        var response = rounds.Select(round => new RoundResponse
        {
            RoundId = round.Id,
            Name = round.Name,
            Status = round.Status.ToString().ToLowerInvariant()
        }).ToList();
        return ServiceResponse<IReadOnlyList<RoundResponse>>.Success(response);
    }

    public async Task<ServiceResponse<RoundResponse>> HeartbeatAsync(
        Guid roundId,
        HeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var round = await _repository.GetAsync(roundId, cancellationToken).ConfigureAwait(false);
            if (round is null)
            {
                throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "Round not found."), 404);
            }

            if (round.Status == RoundStatus.Closed)
            {
                throw new DomainException(new DomainError(ErrorCodes.InvalidRequest, "Round is closed."), 409);
            }

            round.Status = RoundStatus.Active;
            await _repository.UpdateAsync(round, cancellationToken).ConfigureAwait(false);
            await _ledger.AppendAsync("round_heartbeat", new { round.Id, request.NodeId }, cancellationToken).ConfigureAwait(false);

            return ServiceResponse<RoundResponse>.Success(new RoundResponse
            {
                RoundId = round.Id,
                Name = round.Name,
                Status = round.Status.ToString().ToLowerInvariant()
            });
        }
        catch (DomainException exc)
        {
            return ServiceResponse<RoundResponse>.Fail(exc.Error, exc.StatusCode, exc.Message);
        }
    }
}
