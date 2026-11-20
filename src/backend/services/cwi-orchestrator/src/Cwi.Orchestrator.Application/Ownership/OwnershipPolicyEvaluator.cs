#nullable enable
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;

namespace Cwi.Orchestrator.Application.Ownership;

public sealed record OwnershipPolicyDecision(bool Allowed, string? Reason);

public interface IOwnershipPolicyEvaluator
{
    OwnershipPolicyDecision EvaluateRequest(OwnershipTransferRequestDto request, string transferType);
    OwnershipPolicyDecision EvaluateRevoke(OwnershipTransferRequest request, string actorId);
}

public sealed class OwnershipPolicyEvaluator : IOwnershipPolicyEvaluator
{
    public OwnershipPolicyDecision EvaluateRequest(OwnershipTransferRequestDto request, string transferType)
    {
        if (string.Equals(request.FromUserId, request.ToUserId, StringComparison.OrdinalIgnoreCase))
        {
            return new OwnershipPolicyDecision(false, "from_user_id and to_user_id must differ.");
        }

        if (transferType == "delegate" && string.IsNullOrWhiteSpace(request.Reason))
        {
            return new OwnershipPolicyDecision(false, "Delegation requires a reason.");
        }

        return new OwnershipPolicyDecision(true, null);
    }

    public OwnershipPolicyDecision EvaluateRevoke(OwnershipTransferRequest request, string actorId)
    {
        if (string.IsNullOrWhiteSpace(actorId))
        {
            return new OwnershipPolicyDecision(false, "actor_id required.");
        }

        if (string.Equals(request.FromUserId, actorId, StringComparison.OrdinalIgnoreCase))
        {
            return new OwnershipPolicyDecision(true, null);
        }

        var approvers = DeserializeList(request.ApproversJson);
        if (approvers.Contains(actorId, StringComparer.OrdinalIgnoreCase))
        {
            return new OwnershipPolicyDecision(true, null);
        }

        return new OwnershipPolicyDecision(false, "actor_id not authorized to revoke.");
    }

    private static List<string> DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }

        var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
        return list ?? new List<string>();
    }
}
#nullable disable
