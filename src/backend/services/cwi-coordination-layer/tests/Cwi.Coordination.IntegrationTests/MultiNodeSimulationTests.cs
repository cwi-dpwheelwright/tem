using System.Net.Http.Json;
using Cwi.Coordination.Application.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Cwi.Coordination.IntegrationTests;

public sealed class MultiNodeSimulationTests
{
    [Fact]
    public async Task Simulates_Partitions_And_Churn()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Transport:RequireClientCertificate"] = "false",
                        ["Coordination:RequireClientCertificate"] = "false",
                        ["Coordination:Admin:RequireToken"] = "false",
                        ["Coordination:Storage:UseInMemory"] = "true"
                    });
                });
            });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Coordination-Protocol", "1.0");

        var nodeIds = Enumerable.Range(1, 5).Select(i => $"node-{i}").ToList();
        foreach (var nodeId in nodeIds)
        {
            var enrollResponse = await client.PostAsJsonAsync(
                "/api/membership/enroll",
                new EnrollmentRequest
                {
                    NodeId = nodeId,
                    CertificateFingerprint = $"fp-{nodeId}",
                    ProtocolVersion = "1.0"
                });
            enrollResponse.EnsureSuccessStatusCode();
        }

        var roundResponse = await client.PostAsJsonAsync(
            "/api/rounds",
            new RoundCreateRequest { Name = "simulation-round" });
        roundResponse.EnsureSuccessStatusCode();
        var round = await roundResponse.Content.ReadFromJsonAsync<RoundResponse>();
        Assert.NotNull(round);

        foreach (var nodeId in nodeIds)
        {
            var heartbeat = await client.PostAsJsonAsync(
                $"/api/rounds/{round!.RoundId}/heartbeat",
                new HeartbeatRequest { NodeId = nodeId });
            heartbeat.EnsureSuccessStatusCode();
        }

        foreach (var nodeId in nodeIds)
        {
            await PostLinkStateAsync(client, nodeId, 0.9, 50, 0.01, 1200);
        }

        await PostLinkStateAsync(client, "node-4", 0.02, 2000, 0.9, 0);
        await PostLinkStateAsync(client, "node-5", 0.02, 2000, 0.9, 0);
        await PostLinkStateAsync(client, "node-4", 0.8, 80, 0.02, 1000);

        var revoke = await client.PostAsJsonAsync(
            "/api/membership/revoke",
            new RevokeRequest { NodeId = "node-5", Reason = "partitioned" });
        revoke.EnsureSuccessStatusCode();

        var members = await client.GetFromJsonAsync<IReadOnlyList<MemberResponse>>("/api/membership");
        Assert.NotNull(members);
        Assert.Contains(members!, member => member.NodeId == "node-5" && member.Status == "revoked");

        var events = await client.GetFromJsonAsync<IReadOnlyList<MeshEventResponse>>("/api/mesh/events?limit=50");
        Assert.NotNull(events);
        Assert.Contains(events!, evt => evt.EventType == "linkdown");
        Assert.Contains(events!, evt => evt.EventType == "linkrecovered");
    }

    private static async Task PostLinkStateAsync(
        HttpClient client,
        string nodeId,
        double signalQuality,
        int latencyMs,
        double packetLoss,
        int bandwidthKbps)
    {
        var response = await client.PostAsJsonAsync(
            "/api/mesh/links",
            new LinkStateUpdateRequest
            {
                NodeId = nodeId,
                Interface = "wifi",
                SignalQuality = signalQuality,
                LatencyMs = latencyMs,
                PacketLoss = packetLoss,
                BandwidthKbps = bandwidthKbps,
                ThroughputKbps = bandwidthKbps
            });

        response.EnsureSuccessStatusCode();
    }
}
