using System.Net;
using System.Text.Json;
using Cwi.Coordination.Grpc;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Cwi.Coordination.IntegrationTests;

public sealed class GrpcCoordinatorTests
{
    [Fact]
    public async Task Grpc_Can_Enroll_And_Create_Round()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

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

        var httpClient = factory.CreateDefaultClient();
        httpClient.DefaultRequestVersion = HttpVersion.Version20;
        httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

        using var channel = GrpcChannel.ForAddress(httpClient.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = httpClient
        });

        var client = new Coordinator.CoordinatorClient(channel);
        var headers = new Metadata
        {
            { "x-coordination-protocol", "1.0" }
        };

        var enroll = await client.EnrollAsync(new EnrollmentRequest
        {
            NodeId = "node-1",
            TenantId = "tenant-1",
            OwnerUserId = "user-1",
            DeviceClass = "personal-core",
            TailscaleIp = "100.100.10.1",
            CapabilitiesJson = "{\"local_llm\":true}",
            HealthJson = "{\"ready\":true}",
            CertFingerprint = "fp-node-1",
            ProtocolVersion = "1.0"
        }, headers);

        Assert.Equal("node-1", enroll.NodeId);

        var members = await client.ListMembersAsync(new ListMembersRequest(), headers);
        var member = Assert.Single(members.Members);
        Assert.Equal("tenant-1", member.TenantId);
        Assert.Equal("user-1", member.OwnerUserId);
        Assert.Equal("personal-core", member.DeviceClass);
        Assert.Equal("100.100.10.1", member.TailscaleIp);
        using var capabilities = JsonDocument.Parse(member.CapabilitiesJson);
        Assert.True(capabilities.RootElement.GetProperty("local_llm").GetBoolean());
        using var health = JsonDocument.Parse(member.HealthJson);
        Assert.True(health.RootElement.GetProperty("ready").GetBoolean());

        var round = await client.CreateRoundAsync(new RoundCreateRequest { Name = "grpc-round" }, headers);
        Guid.Parse(round.RoundId);
        Assert.Equal("grpc-round", round.Name);

        var heartbeat = await client.HeartbeatAsync(new HeartbeatRequest
        {
            RoundId = round.RoundId,
            NodeId = "node-1",
            Status = "active"
        }, headers);

        Assert.Equal("grpc-round", heartbeat.Name);
        Assert.False(string.IsNullOrWhiteSpace(heartbeat.RoundId));
    }
}
