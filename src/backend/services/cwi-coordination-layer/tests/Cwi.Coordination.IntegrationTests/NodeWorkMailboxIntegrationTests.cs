using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cwi.Coordination.Api.Security;
using Cwi.Coordination.Application.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cwi.Coordination.IntegrationTests;

public sealed class NodeWorkMailboxIntegrationTests
{
    [Fact]
    public async Task NodeWork_Http_Flow_Requires_Admin_Create_And_Completes_End_To_End()
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
                        ["Coordination:Admin:RequireToken"] = "true",
                        ["Coordination:Admin:Token"] = "secret",
                        ["Coordination:Storage:UseInMemory"] = "true"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.Replace(ServiceDescriptor.Singleton(new AdminAuthorizationOptions
                    {
                        RequireToken = true,
                        HeaderName = "X-Admin-Token",
                        Token = "secret"
                    }));
                    services.Replace(ServiceDescriptor.Singleton<AdminAuthorizationService>(sp =>
                        new AdminAuthorizationService(sp.GetRequiredService<AdminAuthorizationOptions>())));
                });
            });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Coordination-Protocol", "1.0");

        var enrollResponse = await client.PostAsJsonAsync(
            "/api/membership/enroll",
            new EnrollmentRequest
            {
                NodeId = "node-home-1",
                TenantId = "tenant-1",
                OwnerUserId = "user-1",
                DeviceClass = "personal-core",
                TailscaleIp = "100.100.10.10",
                CertificateFingerprint = "fp-node-home-1",
                ProtocolVersion = "1.0"
            });
        enrollResponse.EnsureSuccessStatusCode();

        var unauthorizedCreate = await client.PostAsJsonAsync(
            "/api/node-work",
            new CreateNodeWorkRequest
            {
                NodeId = "node-home-1",
                TenantId = "tenant-1",
                TaskType = "chat_history.import"
            });
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedCreate.StatusCode);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/node-work")
        {
            Content = JsonContent.Create(new CreateNodeWorkRequest
            {
                WorkId = "work-http-1",
                NodeId = "node-home-1",
                TenantId = "tenant-1",
                UserId = "user-1",
                TaskType = "chat_history.import",
                Payload = new Dictionary<string, object?> { ["source"] = "chatgpt" }
            })
        };
        createRequest.Headers.Add("X-Admin-Token", "secret");
        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<NodeWorkItemResponse>();
        Assert.NotNull(created);
        Assert.Equal("queued", created!.Status);

        var leaseResponse = await client.PostAsJsonAsync(
            "/api/node-work/lease",
            new LeaseNodeWorkRequest
            {
                NodeId = "node-home-1",
                MaxItems = 1,
                LeaseDurationSeconds = 120
            });
        leaseResponse.EnsureSuccessStatusCode();
        var leased = await leaseResponse.Content.ReadFromJsonAsync<LeaseNodeWorkResponse>();
        Assert.NotNull(leased);
        Assert.Single(leased!.Items);

        var workItem = leased.Items.Single();

        var ackResponse = await client.PostAsJsonAsync(
            $"/api/node-work/{workItem.WorkId}/ack",
            new AckNodeWorkRequest
            {
                NodeId = "node-home-1",
                LeaseId = workItem.LeaseId!
            });
        ackResponse.EnsureSuccessStatusCode();

        var resultResponse = await client.PostAsJsonAsync(
            $"/api/node-work/{workItem.WorkId}/result",
            new CompleteNodeWorkRequest
            {
                NodeId = "node-home-1",
                LeaseId = workItem.LeaseId!,
                Status = "completed",
                Result = new Dictionary<string, object?> { ["processed_chunks"] = 8 }
            });
        resultResponse.EnsureSuccessStatusCode();

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/node-work?node_id=node-home-1");
        listRequest.Headers.Add("X-Admin-Token", "secret");
        var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        var items = await listResponse.Content.ReadFromJsonAsync<List<NodeWorkItemResponse>>();
        Assert.NotNull(items);
        Assert.Single(items!);
        Assert.Equal("completed", items[0].Status);
        Assert.True(items[0].Result.TryGetValue("processed_chunks", out var processedChunks));
        Assert.Equal(8, ((JsonElement)processedChunks!).GetInt32());
    }
}
