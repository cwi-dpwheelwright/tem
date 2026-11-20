using System.Net;
using System.Net.Http.Json;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.DataAccess;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class OperatorMemoryControllerTests
{
    [Fact]
    public async Task Ingest_accepts_events_and_profile_can_be_fetched()
    {
        var factory = CreateFactory("operator-memory");
        using var client = factory.CreateClient();

        var request = new OperatorMemoryIngestRequestDto
        {
            TenantId = "tenant-1",
            OperatorId = "operator-1",
            Consent = new OperatorConsentDto { Granted = true },
            Signals = new List<OperatorSignalDto>
            {
                new()
                {
                    SignalType = "correction",
                    Value = 1,
                    Unit = "count",
                    OccurredUtc = DateTimeOffset.UtcNow
                }
            },
            Performance = new List<OperatorPerformanceDto>
            {
                new()
                {
                    Metric = "response_latency_ms",
                    Value = 450,
                    Unit = "ms",
                    WindowUtc = DateTimeOffset.UtcNow
                }
            }
        };

        var response = await client.PostAsJsonAsync("/api/operator-memory/ingest", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OperatorMemoryIngestResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Accepted);
        Assert.NotNull(payload.Profile);

        var profileResponse = await client.GetAsync("/api/operator-memory/profile?tenant_id=tenant-1&operator_id=operator-1");
        Assert.Equal(HttpStatusCode.OK, profileResponse.StatusCode);
        var profile = await profileResponse.Content.ReadFromJsonAsync<OperatorProfileSummaryDto>();
        Assert.NotNull(profile);
        Assert.Equal("tenant-1", profile!.TenantId);
        Assert.Equal("operator-1", profile.OperatorId);
    }

    [Fact]
    public async Task Ingest_rejects_without_consent()
    {
        var factory = CreateFactory("operator-memory-consent");
        using var client = factory.CreateClient();

        var request = new OperatorMemoryIngestRequestDto
        {
            TenantId = "tenant-1",
            OperatorId = "operator-1",
            Signals = new List<OperatorSignalDto>
            {
                new()
                {
                    SignalType = "correction",
                    Value = 1,
                    Unit = "count",
                    OccurredUtc = DateTimeOffset.UtcNow
                }
            }
        };

        var response = await client.PostAsJsonAsync("/api/operator-memory/ingest", request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
                });
            });
    }

    private static void ConfigureEnvironment()
    {
        Environment.SetEnvironmentVariable("REGISTRY", "registry.example.com");
        Environment.SetEnvironmentVariable("CWI_ORCHESTRATOR_TAG", "test");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_HTTP_PORT", "8081");
        Environment.SetEnvironmentVariable("PACKS_BASE_URL", "http://localhost:8082");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_DB_CONNECTION", "Host=localhost;Database=test;Username=test;Password=test");
        Environment.SetEnvironmentVariable("VECTOR_SHARD_1_URL", "http://localhost:6333/");
        Environment.SetEnvironmentVariable("VECTOR_SHARD_2_URL", "http://localhost:6333/");
        Environment.SetEnvironmentVariable("VECTOR_EMBEDDING_DIMENSION", "64");
        Environment.SetEnvironmentVariable("MODEL_BASE_URL", "http://localhost:11434/");
        Environment.SetEnvironmentVariable("MODEL_NAME", "llama3");
        Environment.SetEnvironmentVariable("MODEL_TIMEOUT_SECONDS", "60");
    }
}
