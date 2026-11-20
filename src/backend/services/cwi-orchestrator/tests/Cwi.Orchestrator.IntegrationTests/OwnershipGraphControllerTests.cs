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

public sealed class OwnershipGraphControllerTests
{
    [Fact]
    public async Task Lineage_and_revocations_return_expected_entries()
    {
        var factory = CreateFactory("ownership-graph");
        using var client = factory.CreateClient();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
            db.OwnershipTransferRequests.AddRange(new List<OwnershipTransferRequest>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    RequestId = "req-1",
                    TenantId = "tenant-1",
                    FromUserId = "owner-1",
                    ToUserId = "owner-2",
                    TransferType = "transfer",
                    Status = "approved",
                    Reason = "scope:Billing; transfer",
                    CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                    UpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-4)
                },
                new()
                {
                    Id = Guid.NewGuid(),
                    RequestId = "req-2",
                    TenantId = "tenant-1",
                    FromUserId = "owner-1",
                    ToUserId = "delegate-1",
                    TransferType = "delegate",
                    Status = "pending",
                    Reason = "scope:Ops; delegation",
                    CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-3),
                    UpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
                }
            });
            db.SaveChanges();
        }

        var lineageResponse = await client.GetAsync("/api/ownership/lineage?tenant_id=tenant-1");
        Assert.Equal(HttpStatusCode.OK, lineageResponse.StatusCode);
        var lineage = await lineageResponse.Content.ReadFromJsonAsync<OwnershipLineageResponseDto>();
        Assert.NotNull(lineage);
        Assert.Contains(lineage!.Edges, edge => edge.Relationship == "successor" && edge.Scope == "Billing");
        Assert.Contains(lineage.Edges, edge => edge.Relationship == "delegate" && edge.Scope == "Ops");

        var revocationResponse = await client.GetAsync("/api/ownership/revocations?tenant_id=tenant-1");
        Assert.Equal(HttpStatusCode.OK, revocationResponse.StatusCode);
        var revocations = await revocationResponse.Content.ReadFromJsonAsync<List<OwnershipRevocationEntryDto>>();
        Assert.NotNull(revocations);
        Assert.Contains(revocations!, entry => entry.RequestId == "req-2" && entry.Status == "pending");
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
        Environment.SetEnvironmentVariable("CWI_GRAPH_DB_DSN", "Host=localhost;Database=test;Username=test;Password=test");
        Environment.SetEnvironmentVariable("VECTOR_SHARD_1_URL", "http://localhost:6333/");
        Environment.SetEnvironmentVariable("VECTOR_SHARD_2_URL", "http://localhost:6333/");
        Environment.SetEnvironmentVariable("VECTOR_EMBEDDING_DIMENSION", "64");
        Environment.SetEnvironmentVariable("MODEL_BASE_URL", "http://localhost:11434/");
        Environment.SetEnvironmentVariable("MODEL_NAME", "llama3");
        Environment.SetEnvironmentVariable("MODEL_TIMEOUT_SECONDS", "60");
    }
}
