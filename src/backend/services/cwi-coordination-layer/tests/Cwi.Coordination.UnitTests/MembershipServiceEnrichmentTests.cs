using System.Text.Json;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Pipeline;
using Cwi.Coordination.Application.Services;
using Cwi.Coordination.DataAccess;
using Cwi.Coordination.Domain.Entities;
using FluentAssertions;

namespace Cwi.Coordination.UnitTests;

public sealed class MembershipServiceEnrichmentTests
{
    [Fact]
    public async Task EnrollAsync_Persists_And_Refreshes_Enriched_Metadata_For_Same_Fingerprint()
    {
        var repo = new InMemoryMembershipRepository();
        var pipeline = new MembershipPipeline(new IPipelineHandler<EnrollmentRequest, EnrollmentResponse>[]
        {
            new MembershipValidationHandler()
        });
        var service = new MembershipService(repo, pipeline);

        var firstResponse = await service.EnrollAsync(new EnrollmentRequest
        {
            NodeId = "node-home-1",
            TenantId = "tenant-1",
            OwnerUserId = "user-1",
            DeviceClass = "personal-core",
            TailscaleIp = "100.100.10.1",
            WatcherNodeId = "node-watch-1",
            Capabilities = new Dictionary<string, object?> { ["local_llm"] = true },
            PackVersions = new Dictionary<string, object?> { ["main"] = "2026.04.04" },
            PolicyProfileVersion = "2.0",
            Health = new Dictionary<string, object?> { ["ready"] = true },
            CertificateFingerprint = "fp-1"
        }, CancellationToken.None);

        var secondResponse = await service.EnrollAsync(new EnrollmentRequest
        {
            NodeId = "node-home-1",
            TenantId = "tenant-1",
            OwnerUserId = "user-1",
            DeviceClass = "personal-core",
            TailscaleIp = "100.100.10.9",
            WatcherNodeId = "node-watch-1",
            Capabilities = new Dictionary<string, object?> { ["local_llm"] = true, ["gpu"] = "available" },
            PackVersions = new Dictionary<string, object?> { ["main"] = "2026.04.05" },
            PolicyProfileVersion = "2.1",
            Health = new Dictionary<string, object?> { ["ready"] = true, ["last_boot"] = "2026-04-04T12:00:00Z" },
            CertificateFingerprint = "fp-1"
        }, CancellationToken.None);

        firstResponse.IsSuccess.Should().BeTrue();
        secondResponse.IsSuccess.Should().BeTrue();

        var member = await repo.GetAsync("node-home-1", CancellationToken.None);
        member.Should().NotBeNull();
        member!.Status.Should().Be(MembershipStatus.Active);
        member.TenantId.Should().Be("tenant-1");
        member.OwnerUserId.Should().Be("user-1");
        member.TailscaleIp.Should().Be("100.100.10.9");
        member.PolicyProfileVersion.Should().Be("2.1");
        member.LastSeenUtc.Should().NotBeNull();

        using var capabilities = JsonDocument.Parse(member.CapabilitiesJson);
        capabilities.RootElement.GetProperty("gpu").GetString().Should().Be("available");

        using var health = JsonDocument.Parse(member.HealthJson);
        health.RootElement.GetProperty("ready").GetBoolean().Should().BeTrue();

        var listResponse = await service.ListAsync(CancellationToken.None);
        listResponse.IsSuccess.Should().BeTrue();
        listResponse.Data.Should().HaveCount(1);
        listResponse.Data![0].LastSeenUtc.Should().NotBeNull();
        ((JsonElement)listResponse.Data[0].Capabilities["gpu"]!).GetString().Should().Be("available");
    }
}
