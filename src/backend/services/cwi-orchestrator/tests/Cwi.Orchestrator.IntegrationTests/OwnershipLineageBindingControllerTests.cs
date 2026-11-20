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

public sealed class OwnershipLineageBindingControllerTests
{
    [Fact]
    public async Task Bind_and_assets_require_break_glass_token()
    {
        var factory = CreateFactory("ownership-lineage");
        using var client = factory.CreateClient();

        var tokenId = "token-lineage";
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

        var request = new OwnershipLineageBindingRequestDto
        {
            TenantId = "tenant-1",
            AssetId = "asset-1",
            OwnerId = "owner-1",
            ParentAssetId = "asset-root",
            Relationship = "owner",
            Reason = "seed"
        };

        var unauthorized = await client.PostAsJsonAsync("/api/ownership/lineage/bind", request);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        client.DefaultRequestHeaders.Add("X-Break-Glass-Token", tokenId);
        var response = await client.PostAsJsonAsync("/api/ownership/lineage/bind", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<OwnershipLineageBindingResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal("bound", payload!.Status);

        var lineageResponse = await client.GetAsync("/api/ownership/lineage/assets?tenant_id=tenant-1");
        Assert.Equal(HttpStatusCode.OK, lineageResponse.StatusCode);
        var lineage = await lineageResponse.Content.ReadFromJsonAsync<OwnershipLineageResponseDto>();
        Assert.NotNull(lineage);
        Assert.Contains(lineage!.Nodes, node => node.NodeId == "asset-1" && node.Role == "Asset");
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
