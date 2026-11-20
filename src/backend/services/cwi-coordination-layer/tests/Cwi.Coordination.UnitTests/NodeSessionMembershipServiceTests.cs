using System.Text.Json;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Cwi.Coordination.DataAccess;
using Cwi.Coordination.Domain.Entities;
using FluentAssertions;

namespace Cwi.Coordination.UnitTests;

public sealed class NodeSessionMembershipServiceTests
{
    [Fact]
    public async Task UpdateFromHelloAndHeartbeat_Persist_Local_Model_Profile()
    {
        var repository = new InMemoryMembershipRepository();
        await repository.AddAsync(new Member
        {
            NodeId = "home-node-01",
            TenantId = "tenant-1",
            OwnerUserId = "user-1",
            CertificateFingerprint = "fp-home-node-01",
            Status = MembershipStatus.Active
        }, CancellationToken.None);
        var service = new NodeSessionMembershipService(repository);

        await service.UpdateFromHelloAsync(
            "home-node-01",
            new NodeHelloMessage
            {
                NodeId = "home-node-01",
                TenantId = "tenant-1",
                OwnerUserId = "user-1",
                Capabilities = ["local_ask", "local_model"],
                CapabilityDetails = new Dictionary<string, object?>
                {
                    ["local_model_profile"] = new Dictionary<string, object?>
                    {
                        ["model"] = "local-8b:q4",
                        ["quantization"] = "Q4_K_M",
                        ["context_tokens"] = 4096,
                        ["max_vram_gb"] = 6
                    }
                }
            },
            CancellationToken.None);
        await service.TouchAsync(
            "home-node-01",
            new NodeHeartbeatMessage
            {
                NodeId = "home-node-01",
                Status = "READY",
                ModelProfile = new Dictionary<string, object?>
                {
                    ["model"] = "local-8b:q4",
                    ["quantization"] = "Q4_K_M"
                },
                Hardware = new Dictionary<string, object?>
                {
                    ["configured_gpu_vram_gb"] = 6
                }
            },
            CancellationToken.None);

        var member = await repository.GetAsync("home-node-01", CancellationToken.None);

        member.Should().NotBeNull();
        using var capabilities = JsonDocument.Parse(member!.CapabilitiesJson);
        capabilities.RootElement.GetProperty("local_model").GetBoolean().Should().BeTrue();
        capabilities.RootElement
            .GetProperty("local_model_profile")
            .GetProperty("quantization")
            .GetString()
            .Should()
            .Be("Q4_K_M");

        using var health = JsonDocument.Parse(member.HealthJson);
        health.RootElement.GetProperty("model_profile").GetProperty("model").GetString().Should().Be("local-8b:q4");
        health.RootElement.GetProperty("hardware").GetProperty("configured_gpu_vram_gb").GetInt32().Should().Be(6);
    }
}
