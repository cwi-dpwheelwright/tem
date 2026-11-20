using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Cwi.Coordination.Api.Security;
using Cwi.Coordination.Application.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cwi.Coordination.IntegrationTests;

public sealed class NodeSessionTransportIntegrationTests
{
    [Fact]
    public async Task NodeSession_WebSocket_Pushes_Work_And_Processes_Result()
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
                PolicyProfileVersion = "1.0",
                ProtocolVersion = "1.0"
            });
        enrollResponse.EnsureSuccessStatusCode();

        var webSocketClient = factory.Server.CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request =>
        {
            request.Headers["X-Coordination-Protocol"] = "1.0";
        };

        using var socket = await webSocketClient.ConnectAsync(
            new Uri("ws://localhost/control/nodes/connect"),
            CancellationToken.None);

        await SendJsonAsync(socket, new NodeHelloMessage
        {
            NodeId = "node-home-1",
            TenantId = "tenant-1",
            OwnerUserId = "user-1",
            FriendlyName = "home-node-1",
            RuntimeVersion = "2026.04.04",
            PolicyProfileVersion = "1.0",
            Capabilities = ["local_ask", "local_action"]
        });

        await Task.Delay(50);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, "/api/node-work")
        {
            Content = JsonContent.Create(new CreateNodeWorkRequest
            {
                WorkId = "work-ws-1",
                RequestId = "req-ws-1",
                TraceId = "trace-ws-1",
                NodeId = "node-home-1",
                TenantId = "tenant-1",
                UserId = "user-1",
                TaskType = "ask.local",
                PolicyScope = "LOCAL_ONLY",
                WorkToken = "token-1",
                Payload = new Dictionary<string, object?> { ["question"] = "hello from ws" }
            })
        };
        createRequest.Headers.Add("X-Admin-Token", "secret");
        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();

        var created = await createResponse.Content.ReadFromJsonAsync<NodeWorkItemResponse>();
        Assert.NotNull(created);
        Assert.Equal("leased", created!.Status);

        var envelope = await ReceiveJsonAsync<NodeWorkEnvelopeMessage>(socket);
        Assert.Equal("work-ws-1", envelope.WorkId);
        Assert.Equal("req-ws-1", envelope.RequestId);
        Assert.Equal("trace-ws-1", envelope.TraceContext.TraceId);
        Assert.Equal("node-home-1", envelope.TargetNodeId);
        Assert.Equal("LOCAL_ONLY", envelope.PolicyScope);
        Assert.Equal("ask", envelope.RequestKind);
        Assert.Equal("ask.local", envelope.TaskType);
        Assert.Equal("hello from ws", ((JsonElement)envelope.Payload["question"]!).GetString());

        await SendJsonAsync(socket, new NodeWorkAckMessage
        {
            WorkId = envelope.WorkId,
            RequestId = envelope.RequestId,
            NodeId = "node-home-1",
            LeaseId = envelope.LeaseId,
            Status = "ACCEPTED"
        });

        await SendJsonAsync(socket, new NodeWorkResultMessage
        {
            WorkId = envelope.WorkId,
            RequestId = envelope.RequestId,
            NodeId = "node-home-1",
            LeaseId = envelope.LeaseId,
            Outcome = "SUCCESS",
            Result = new Dictionary<string, object?> { ["answer"] = "done" }
        });

        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/node-work?node_id=node-home-1");
        listRequest.Headers.Add("X-Admin-Token", "secret");
        var listResponse = await client.SendAsync(listRequest);
        listResponse.EnsureSuccessStatusCode();
        var items = await listResponse.Content.ReadFromJsonAsync<List<NodeWorkItemResponse>>();
        Assert.NotNull(items);
        Assert.Single(items!);
        Assert.Equal("completed", items[0].Status);
        Assert.Equal("req-ws-1", items[0].RequestId);
        Assert.Equal("trace-ws-1", items[0].TraceId);
        Assert.True(items[0].Result.TryGetValue("answer", out var answer));
        Assert.Equal("done", ((JsonElement)answer!).GetString());
    }

    private static async Task SendJsonAsync(WebSocket socket, object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
    }

    private static async Task<T> ReceiveJsonAsync<T>(WebSocket socket)
    {
        var buffer = new byte[4096];
        await using var stream = new MemoryStream();

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Socket closed before message was received.");
            }

            await stream.WriteAsync(buffer.AsMemory(0, result.Count), CancellationToken.None).ConfigureAwait(false);
            if (result.EndOfMessage)
            {
                var json = Encoding.UTF8.GetString(stream.ToArray());
                return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException("Failed to deserialize WebSocket payload.");
            }
        }
    }
}
