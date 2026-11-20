using System.Net;
using System.Net.Http.Json;
using Cwi.Orchestrator.Application.Retention;
using Cwi.Orchestrator.DataAccess;
using Cwi.Orchestrator.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class RetentionPolicyControllerTests
{
    [Fact]
    public async Task Get_returns_retention_policy_when_break_glass_token_present()
    {
        var factory = CreateFactory("retention-policy");
        using var client = factory.CreateClient();

        var tokenId = "token-retention";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
            db.BreakGlassTokens.Add(new BreakGlassToken
            {
                Id = Guid.NewGuid(),
                TokenId = tokenId,
                TenantId = "tenant-1",
                UserId = "user-1",
                Role = "admin",
                Scope = "admin",
                Reason = "test",
                CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5)
            });
            db.SaveChanges();
        }

        client.DefaultRequestHeaders.Add("X-Break-Glass-Token", tokenId);
        var response = await client.GetAsync("/api/admin/retention");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<RetentionPolicyOptions>();
        Assert.NotNull(payload);
        Assert.Equal(7, payload!.TelemetryDays);
        Assert.Equal(30, payload.LedgerDays);
        Assert.Equal(14, payload.TraceDays);
        Assert.True(payload.PurgeEnabled);
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
        Environment.SetEnvironmentVariable("RETENTION_TELEMETRY_DAYS", "7");
        Environment.SetEnvironmentVariable("RETENTION_LEDGER_DAYS", "30");
        Environment.SetEnvironmentVariable("RETENTION_TRACE_DAYS", "14");
        Environment.SetEnvironmentVariable("RETENTION_PURGE_ENABLED", "true");
    }
}
