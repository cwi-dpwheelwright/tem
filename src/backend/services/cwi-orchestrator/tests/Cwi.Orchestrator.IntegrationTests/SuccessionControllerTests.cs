using System.Net;
using System.Net.Http.Json;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.DataAccess;
using Cwi.Orchestrator.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class SuccessionControllerTests
{
    [Fact]
    public async Task Create_trigger_restore_succession_plan()
    {
        var factory = CreateFactory("succession");
        using var client = factory.CreateClient();

        var tokenId = "token-succession";
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
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(10)
            });
            db.SaveChanges();
        }

        client.DefaultRequestHeaders.Add("X-Break-Glass-Token", tokenId);

        var planRequest = new SuccessionPlanRequestDto
        {
            TenantId = "tenant-1",
            AssetId = "asset-1",
            CurrentOwnerId = "owner-1",
            Successors = new List<SuccessionSuccessorDto>
            {
                new()
                {
                    OwnerId = "owner-2",
                    Order = 1,
                    Trigger = "retirement",
                    ApprovalRequired = false
                }
            }
        };

        var planResponse = await client.PostAsJsonAsync("/api/succession/plans", planRequest);
        Assert.Equal(HttpStatusCode.OK, planResponse.StatusCode);
        var plan = await planResponse.Content.ReadFromJsonAsync<SuccessionPlanResponseDto>();
        Assert.NotNull(plan);
        Assert.False(string.IsNullOrWhiteSpace(plan!.PlanId));

        var triggerResponse = await client.PostAsJsonAsync("/api/succession/plans/trigger", new SuccessionTriggerRequestDto
        {
            TenantId = "tenant-1",
            PlanId = plan.PlanId,
            ActorId = "owner-1",
            Reason = "retirement"
        });
        Assert.Equal(HttpStatusCode.OK, triggerResponse.StatusCode);
        var trigger = await triggerResponse.Content.ReadFromJsonAsync<SuccessionTriggerResponseDto>();
        Assert.NotNull(trigger);
        Assert.Equal("executed", trigger!.Status);
        Assert.False(string.IsNullOrWhiteSpace(trigger.ContinuitySnapshotId));

        var restoreResponse = await client.PostAsJsonAsync("/api/succession/plans/restore", new SuccessionRestoreRequestDto
        {
            TenantId = "tenant-1",
            SnapshotId = trigger.ContinuitySnapshotId!,
            ActorId = "owner-1",
            Reason = "rollback"
        });
        Assert.Equal(HttpStatusCode.OK, restoreResponse.StatusCode);
        var restore = await restoreResponse.Content.ReadFromJsonAsync<SuccessionRestoreResponseDto>();
        Assert.NotNull(restore);
        Assert.Equal("restored", restore!.Status);
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
