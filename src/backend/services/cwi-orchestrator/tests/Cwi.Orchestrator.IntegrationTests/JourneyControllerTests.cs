using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Cwi.Common.Journey;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Api.Journey;
using Cwi.Orchestrator.DataAccess;
using Cwi.Orchestrator.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class JourneyControllerTests
{
    [Fact]
    public async Task ListByRequestId_ReturnsOnlyCallerScopedEvents()
    {
        var factory = CreateFactory("journey-list");
        using var client = CreateAuthorizedClient(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
            db.JourneyEventRecords.Add(new JourneyEventRecord
            {
                EventId = "evt-1",
                StreamId = "1-0",
                RequestId = "request-1",
                TraceId = "trace-1",
                TenantId = "tenant-1",
                UserId = "user-1",
                Service = "cwi-router",
                Component = "router",
                Phase = "request_received",
                CreatedUtc = DateTimeOffset.UtcNow,
                MetadataJson = "{}"
            });
            db.JourneyEventRecords.Add(new JourneyEventRecord
            {
                EventId = "evt-2",
                StreamId = "2-0",
                RequestId = "request-1",
                TraceId = "trace-2",
                TenantId = "tenant-2",
                UserId = "user-2",
                Service = "cwi-router",
                Component = "router",
                Phase = "request_received",
                CreatedUtc = DateTimeOffset.UtcNow,
                MetadataJson = "{}"
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/journey/requests/request-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("request-1", document.RootElement.GetProperty("request_id").GetString());
        var items = document.RootElement.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal("evt-1", items[0].GetProperty("event_id").GetString());
    }

    [Fact]
    public async Task StreamByRequestId_PushesLiveEvents()
    {
        var factory = CreateFactory("journey-stream");
        using var client = CreateAuthorizedClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/journey/requests/request-live/stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "valid");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stream = await response.Content.ReadAsStreamAsync();
        var readTask = ReadUntilContainsAsync(stream, "\"event_id\":\"evt-live\"");

        await Task.Delay(100);
        var broadcaster = factory.Services.GetRequiredService<JourneyBroadcaster>();
        broadcaster.Publish(new JourneyEvent
        {
            EventId = "evt-live",
            RequestId = "request-live",
            TraceId = "trace-live",
            TenantId = "tenant-1",
            UserId = "user-1",
            Service = "cwi-orchestrator",
            Component = "orchestrator",
            Phase = "response_ready",
            Direction = "response",
            CreatedUtc = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object?>()
        });

        var payload = await readTask;
        Assert.Contains("event: journey", payload);
        Assert.Contains("\"event_id\":\"evt-live\"", payload);
    }

    private static WebApplicationFactory<Program> CreateFactory(string dbName)
    {
        ConfigureEnvironment();

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(DbContextOptions<OrchestratorDbContext>));
                    services.RemoveAll(typeof(OrchestratorDbContext));
                    services.AddDbContext<OrchestratorDbContext>(options =>
                    {
                        options.UseInMemoryDatabase(dbName);
                    });

                    services.RemoveAll(typeof(AuthOptions));
                    services.AddSingleton(new AuthOptions { AuthRequired = true });
                    services.RemoveAll(typeof(IJwtValidator));
                    services.AddSingleton<IJwtValidator>(new AllowAllJwtValidator());
                });
            });
    }

    private static HttpClient CreateAuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "valid");
        return client;
    }

    private static async Task<string> ReadUntilContainsAsync(Stream stream, string needle)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var reader = new StreamReader(stream);
        var buffer = string.Empty;

        while (!timeout.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(timeout.Token);
            if (line is null)
            {
                break;
            }

            buffer += line + "\n";
            if (buffer.Contains(needle, StringComparison.Ordinal))
            {
                return buffer;
            }
        }

        throw new TimeoutException($"Timed out waiting for SSE payload containing {needle}.");
    }

    private static void ConfigureEnvironment()
    {
        Environment.SetEnvironmentVariable("REGISTRY", "registry.example.com");
        Environment.SetEnvironmentVariable("CWI_ORCHESTRATOR_TAG", "test");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_HTTP_PORT", "8081");
        Environment.SetEnvironmentVariable("PACKS_BASE_URL", "http://localhost:8082");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_DB_CONNECTION", "Host=localhost;Database=test;Username=test;Password=test");
        Environment.SetEnvironmentVariable("CWI_GRAPH_DB_DSN", "Host=localhost;Database=test;Username=test;Password=test");
        Environment.SetEnvironmentVariable("VECTOR_SHARD_1_URL", "http://localhost:6333/");
        Environment.SetEnvironmentVariable("VECTOR_SHARD_2_URL", "http://localhost:6333/");
        Environment.SetEnvironmentVariable("VECTOR_EMBEDDING_DIMENSION", "64");
        Environment.SetEnvironmentVariable("MODEL_BASE_URL", "http://localhost:11434/");
        Environment.SetEnvironmentVariable("MODEL_NAME", "llama3");
        Environment.SetEnvironmentVariable("MODEL_TIMEOUT_SECONDS", "60");
    }

    private sealed class AllowAllJwtValidator : IJwtValidator
    {
        public Task<JwtValidationResult> ValidateAsync(string token, CancellationToken cancellationToken)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", "user-1"),
                new Claim("tenant_id", "tenant-1")
            }, "jwt"));

            return Task.FromResult(JwtValidationResult.Success(principal));
        }
    }
}
