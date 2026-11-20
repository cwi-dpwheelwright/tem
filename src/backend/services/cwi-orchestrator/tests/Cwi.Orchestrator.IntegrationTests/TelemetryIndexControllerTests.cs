using System.Net;
using System.Net.Http.Json;
using Cwi.Orchestrator.Application.Telemetry;
using Cwi.Orchestrator.DataAccess;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class TelemetryIndexControllerTests
{
    [Fact]
    public async Task Get_returns_index_entries_for_tenant()
    {
        var factory = CreateFactory("telemetry-index");
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<TelemetryIndexStore>();
            store.Record(new TelemetryIndexEntry
            {
                IngestId = "ingest-1",
                TenantId = "tenant-a",
                WindowStartUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                WindowEndUtc = DateTimeOffset.UtcNow.AddMinutes(-4),
                CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-3)
            });
            store.Record(new TelemetryIndexEntry
            {
                IngestId = "ingest-2",
                TenantId = "tenant-b",
                WindowStartUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
                WindowEndUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                CreatedUtc = DateTimeOffset.UtcNow
            });
        }

        var response = await client.GetAsync("/api/telemetry/index?tenant_id=tenant-a&limit=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TelemetryIndexPage>();
        Assert.NotNull(payload);
        Assert.True(payload!.Enabled);
        Assert.Equal(1, payload.Limit);
        Assert.Single(payload.Items);
        Assert.Equal("tenant-a", payload.Items[0].TenantId);
    }

    [Fact]
    public async Task Get_returns_empty_when_disabled()
    {
        var factory = CreateFactory("telemetry-disabled", enabled: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/telemetry/index?limit=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TelemetryIndexPage>();
        Assert.NotNull(payload);
        Assert.False(payload!.Enabled);
        Assert.Empty(payload.Items);
    }

    private static WebApplicationFactory<Program> CreateFactory(string dbName, bool enabled = true)
    {
        ConfigureEnvironment(enabled);

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

    private static void ConfigureEnvironment(bool enabled)
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
        Environment.SetEnvironmentVariable("TELEMETRY_INDEX_ENABLED", enabled ? "true" : "false");
        Environment.SetEnvironmentVariable("TELEMETRY_INDEX_MAX_ENTRIES", "100");
    }
}
