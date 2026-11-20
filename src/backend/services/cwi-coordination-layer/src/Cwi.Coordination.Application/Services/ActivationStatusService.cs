using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class ActivationStatusService
{
    private readonly IMembershipRepository _repository;

    public ActivationStatusService(IMembershipRepository repository)
    {
        _repository = repository;
    }

    public async Task<ServiceResponse<ActivationStatusResponse>> GetStatusAsync(
        string? nodeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            return ServiceResponse<ActivationStatusResponse>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "node_id required."),
                statusCode: 400,
                message: "node_id required");
        }

        var member = await _repository.GetAsync(nodeId, cancellationToken).ConfigureAwait(false);
        var status = member?.Status ?? MembershipStatus.Pending;
        var response = new ActivationStatusResponse
        {
            NodeId = nodeId,
            Status = member is null ? "unknown" : status.ToString().ToLowerInvariant(),
            RevokedUtc = member?.RevokedUtc,
            CheckedUtc = DateTimeOffset.UtcNow
        };

        return ServiceResponse<ActivationStatusResponse>.Success(response);
    }
}
