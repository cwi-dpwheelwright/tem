#nullable enable
using System.Net;
using System.Net.Http.Json;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.DataAccess;
using Cwi.Orchestrator.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class LedgerEventsQueryTests
{
    [Fact]
    public async Task Events_ReturnsFilteredResults()
    {
        var factory = CreateFactory("ledger-query");
        using var client = factory.CreateClient();

        var now = DateTimeOffset.UtcNow;
        await SeedLedgerAsync(factory.Services, new LedgerEvent
        {
            Id = Guid.NewGuid(),
            TraceId = "trace-a",
            EventType = "break_glass.issued",
            PayloadJson = "{\"tenant_id\":\"tenant-a\"}",
            PrevHash = string.Empty,
            Hash = "hash-a",
            CreatedUtc = now.AddMinutes(-5)
        });
        await SeedLedgerAsync(factory.Services, new LedgerEvent
        {
            Id = Guid.NewGuid(),
            TraceId = "trace-b",
            EventType = "break_glass.issued",
            PayloadJson = "{\"tenant_id\":\"tenant-b\"}",
            PrevHash = string.Empty,
            Hash = "hash-b",
            CreatedUtc = now.AddMinutes(-4)
        });
        await SeedLedgerAsync(factory.Services, new LedgerEvent
        {
            Id = Guid.NewGuid(),
            TraceId = "trace-c",
            EventType = "pack.activation",
            PayloadJson = "{\"TenantId\":\"tenant-a\"}",
            PrevHash = string.Empty,
            Hash = "hash-c",
            CreatedUtc = now.AddMinutes(-3)
        });

        var startUtc = Uri.EscapeDataString(now.AddMinutes(-10).ToString("O"));
        var endUtc = Uri.EscapeDataString(now.AddMinutes(1).ToString("O"));
        var response = await client.GetAsync($"/api/ledger/events?tenant_id=tenant-a&event_type=break_glass.issued&start_utc={startUtc}&end_utc={endUtc}&page=1&page_size=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<LedgerEventPage>();
        Assert.NotNull(payload);
        Assert.Equal(1, payload!.Total);
        Assert.Single(payload.Items);
        Assert.Equal("trace-a", payload.Items[0].TraceId);
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

    private static async Task SeedLedgerAsync(IServiceProvider services, LedgerEvent ledgerEvent)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        db.LedgerEvents.Add(ledgerEvent);
        await db.SaveChangesAsync().ConfigureAwait(false);
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
#nullable disable
