using System.Text.Json;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Domain.Entities;
using Cwi.Coordination.Domain.Repositories;

namespace Cwi.Coordination.Application.Services;

public sealed class NodeSessionMembershipService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IMembershipRepository _membershipRepository;

    public NodeSessionMembershipService(IMembershipRepository membershipRepository)
    {
        _membershipRepository = membershipRepository;
    }

    public async Task<NodeHelloValidationResult> ValidateHelloAsync(
        NodeHelloMessage hello,
        CancellationToken cancellationToken)
    {
        var member = await _membershipRepository.GetAsync(hello.NodeId, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            return new NodeHelloValidationResult
            {
                IsValid = false,
                Reason = "node_not_enrolled"
            };
        }

        if (member.Status != MembershipStatus.Active)
        {
            return new NodeHelloValidationResult
            {
                IsValid = false,
                Reason = "node_not_active"
            };
        }

        if (!string.IsNullOrWhiteSpace(member.TenantId)
            && !string.Equals(member.TenantId, hello.TenantId, StringComparison.Ordinal))
        {
            return new NodeHelloValidationResult
            {
                IsValid = false,
                Reason = "tenant_mismatch"
            };
        }

        if (!string.IsNullOrWhiteSpace(member.OwnerUserId)
            && !string.Equals(member.OwnerUserId, hello.OwnerUserId, StringComparison.Ordinal))
        {
            return new NodeHelloValidationResult
            {
                IsValid = false,
                Reason = "owner_mismatch"
            };
        }

        if (!string.IsNullOrWhiteSpace(member.PolicyProfileVersion)
            && !string.IsNullOrWhiteSpace(hello.PolicyProfileVersion)
            && !string.Equals(member.PolicyProfileVersion, hello.PolicyProfileVersion, StringComparison.Ordinal))
        {
            return new NodeHelloValidationResult
            {
                IsValid = false,
                Reason = "policy_profile_mismatch"
            };
        }

        return new NodeHelloValidationResult
        {
            IsValid = true
        };
    }

    public async Task<bool> UpdateFromHelloAsync(
        string nodeId,
        NodeHelloMessage hello,
        CancellationToken cancellationToken)
    {
        var member = await _membershipRepository.GetAsync(nodeId, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            return false;
        }

        member.LastSeenUtc = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(hello.PolicyProfileVersion))
        {
            member.PolicyProfileVersion = hello.PolicyProfileVersion;
        }

        if (hello.Capabilities.Count > 0 || hello.CapabilityDetails.Count > 0)
        {
            var capabilities = hello.Capabilities.ToDictionary(capability => capability, _ => (object?)true);
            foreach (var item in hello.CapabilityDetails)
            {
                capabilities[item.Key] = item.Value;
            }

            member.CapabilitiesJson = JsonSerializer.Serialize(
                capabilities,
                JsonOptions);
        }

        member.HealthJson = JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["session_status"] = "connected",
                ["friendly_name"] = hello.FriendlyName,
                ["runtime_version"] = hello.RuntimeVersion
            },
            JsonOptions);
        await _membershipRepository.UpdateAsync(member, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task TouchAsync(
        string nodeId,
        NodeHeartbeatMessage? heartbeat,
        CancellationToken cancellationToken)
    {
        var member = await _membershipRepository.GetAsync(nodeId, cancellationToken).ConfigureAwait(false);
        if (member is null)
        {
            return;
        }

        member.LastSeenUtc = DateTimeOffset.UtcNow;
        if (heartbeat is not null)
        {
            if (!string.IsNullOrWhiteSpace(heartbeat.PolicyProfileVersion))
            {
                member.PolicyProfileVersion = heartbeat.PolicyProfileVersion;
            }

            member.HealthJson = JsonSerializer.Serialize(
                new Dictionary<string, object?>
                {
                    ["status"] = heartbeat.Status,
                    ["cpu_summary"] = heartbeat.CpuSummary,
                    ["memory_summary"] = heartbeat.MemorySummary,
                    ["runtime_version"] = heartbeat.RuntimeVersion,
                    ["policy_profile_version"] = heartbeat.PolicyProfileVersion,
                    ["local_privacy_mode"] = heartbeat.LocalPrivacyMode,
                    ["active_session_count"] = heartbeat.ActiveSessionCount,
                    ["model_profile"] = heartbeat.ModelProfile,
                    ["hardware"] = heartbeat.Hardware
                },
                JsonOptions);
        }

        await _membershipRepository.UpdateAsync(member, cancellationToken).ConfigureAwait(false);
    }
}
