using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class BackhaulPolicyService
{
    private readonly IBackhaulPolicyRepository _repository;
    private readonly LedgerService _ledgerService;

    public BackhaulPolicyService(IBackhaulPolicyRepository repository, LedgerService ledgerService)
    {
        _repository = repository;
        _ledgerService = ledgerService;
    }

    public async Task<ServiceResponse<BackhaulPolicyResponse>> GetPolicyAsync(CancellationToken cancellationToken)
    {
        var policy = await _repository.GetAsync(cancellationToken).ConfigureAwait(false);
        return ServiceResponse<BackhaulPolicyResponse>.Success(Map(policy));
    }

    public async Task<ServiceResponse<BackhaulPolicyResponse>> SetPolicyAsync(
        BackhaulPolicyRequest request,
        CancellationToken cancellationToken)
    {
        var policy = new BackhaulPolicy
        {
            RequireAuthenticatedChannel = request.RequireAuthenticatedChannel,
            AllowedInterfaces = request.AllowedInterfaces.Select(ParseInterface).ToList(),
            DeniedInterfaces = request.DeniedInterfaces.Select(ParseInterface).ToList(),
            RevokedPeers = request.RevokedPeers.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList(),
            PolicyVersion = string.IsNullOrWhiteSpace(request.PolicyVersion) ? "1.0" : request.PolicyVersion
        };

        var saved = await _repository.SetAsync(policy, cancellationToken).ConfigureAwait(false);
        await _ledgerService.AppendAsync("backhaul.policy.set", new
        {
            require_authenticated_channel = saved.RequireAuthenticatedChannel,
            allowed_interfaces = saved.AllowedInterfaces.Select(i => i.ToString().ToLowerInvariant()).ToList(),
            denied_interfaces = saved.DeniedInterfaces.Select(i => i.ToString().ToLowerInvariant()).ToList(),
            revoked_peers = saved.RevokedPeers,
            policy_version = saved.PolicyVersion,
            observed_utc = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);
        return ServiceResponse<BackhaulPolicyResponse>.Success(Map(saved));
    }

    public async Task<ServiceResponse<BackhaulDecisionResponse>> EvaluateAsync(
        BackhaulDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var policy = await _repository.GetAsync(cancellationToken).ConfigureAwait(false);
        var interfaceKind = ParseInterface(request.Interface);

        if (policy.RequireAuthenticatedChannel && !request.Authenticated)
        {
            return await DenyAsync(policy, request, "authenticated channel required", cancellationToken)
                .ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(request.PeerId)
            && policy.RevokedPeers.Contains(request.PeerId.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            return await DenyAsync(policy, request, "peer revoked", cancellationToken).ConfigureAwait(false);
        }

        if (policy.DeniedInterfaces.Count > 0 && policy.DeniedInterfaces.Contains(interfaceKind))
        {
            return await DenyAsync(policy, request, "interface denied", cancellationToken).ConfigureAwait(false);
        }

        if (policy.AllowedInterfaces.Count > 0 && !policy.AllowedInterfaces.Contains(interfaceKind))
        {
            return await DenyAsync(policy, request, "interface not permitted", cancellationToken)
                .ConfigureAwait(false);
        }

        var success = new BackhaulDecisionResponse
        {
            Allowed = true,
            Reason = "allowed"
        };

        await _ledgerService.AppendAsync("backhaul.decision", new
        {
            interface_kind = interfaceKind.ToString().ToLowerInvariant(),
            peer_id = request.PeerId,
            authenticated = request.Authenticated,
            allowed = true,
            reason = "allowed",
            policy_version = policy.PolicyVersion,
            observed_utc = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<BackhaulDecisionResponse>.Success(success);
    }

    private static BackhaulPolicyResponse Map(BackhaulPolicy policy)
    {
        return new BackhaulPolicyResponse
        {
            RequireAuthenticatedChannel = policy.RequireAuthenticatedChannel,
            AllowedInterfaces = policy.AllowedInterfaces.Select(i => i.ToString().ToLowerInvariant()).ToList(),
            DeniedInterfaces = policy.DeniedInterfaces.Select(i => i.ToString().ToLowerInvariant()).ToList(),
            RevokedPeers = policy.RevokedPeers,
            PolicyVersion = policy.PolicyVersion
        };
    }

    private async Task<ServiceResponse<BackhaulDecisionResponse>> DenyAsync(
        BackhaulPolicy policy,
        BackhaulDecisionRequest request,
        string reason,
        CancellationToken cancellationToken)
    {
        await _ledgerService.AppendAsync("backhaul.decision", new
        {
            interface_kind = ParseInterface(request.Interface).ToString().ToLowerInvariant(),
            peer_id = request.PeerId,
            authenticated = request.Authenticated,
            allowed = false,
            reason,
            policy_version = policy.PolicyVersion,
            observed_utc = DateTimeOffset.UtcNow
        }, cancellationToken).ConfigureAwait(false);

        return ServiceResponse<BackhaulDecisionResponse>.Fail(
            new DomainError(ErrorCodes.Forbidden, reason),
            statusCode: 403,
            message: reason);
    }

    private static InterfaceKind ParseInterface(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return InterfaceKind.Unknown;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "wifi" => InterfaceKind.Wifi,
            "cellular" => InterfaceKind.Cellular,
            "ethernet" => InterfaceKind.Ethernet,
            "satellite" => InterfaceKind.Satellite,
            "lora" => InterfaceKind.LoRa,
            _ => InterfaceKind.Unknown
        };
    }
}
