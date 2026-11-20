using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class DegradationEventService
{
    private readonly IDegradationEventRepository _repository;

    public DegradationEventService(IDegradationEventRepository repository)
    {
        _repository = repository;
    }

    public async Task<ServiceResponse<DegradationEventReceipt>> AppendAsync(
        DegradationEventRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EventId))
        {
            return ServiceResponse<DegradationEventReceipt>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "event_id required."),
                statusCode: 400,
                message: "event_id required");
        }

        if (string.IsNullOrWhiteSpace(request.NodeId))
        {
            return ServiceResponse<DegradationEventReceipt>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "node_id required."),
                statusCode: 400,
                message: "node_id required");
        }

        if (request.Severity < 0 || request.Severity > 1)
        {
            return ServiceResponse<DegradationEventReceipt>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "severity must be between 0 and 1."),
                statusCode: 400,
                message: "severity must be between 0 and 1");
        }

        var degradationEvent = new DegradationEvent
        {
            EventId = request.EventId,
            NodeId = request.NodeId,
            ObservedUtc = request.ObservedUtc,
            Severity = request.Severity,
            Details = request.Details ?? new Dictionary<string, object?>(),
            Provenance = request.Provenance
        };

        await _repository.AppendAsync(degradationEvent, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<DegradationEventReceipt>.Success(new DegradationEventReceipt
        {
            EventId = degradationEvent.EventId,
            ReceivedUtc = degradationEvent.ReceivedUtc
        });
    }
}
