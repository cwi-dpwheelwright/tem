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

public sealed class ArtifactRegistryControllerTests
{
    [Fact]
    public async Task Register_list_get_require_break_glass_token()
    {
        var factory = CreateFactory("artifact-registry");
        using var client = factory.CreateClient();

        var tokenId = "token-artifacts";
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

        var request = new ArtifactRegistrationRequestDto
        {
            TenantId = "tenant-1",
            ArtifactId = "artifact-1",
            ArtifactType = "report",
            Version = "v1",
            Hash = "hash-1",
            Title = "Quarterly Report",
            ProvenanceRefs = new[] { "trace-1" }
        };

        var unauthorized = await client.PostAsJsonAsync("/api/artifacts/register", request);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        client.DefaultRequestHeaders.Add("X-Break-Glass-Token", tokenId);
        var response = await client.PostAsJsonAsync("/api/artifacts/register", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ArtifactRegistrationResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal("artifact-1", payload!.ArtifactId);

        var listResponse = await client.GetAsync("/api/artifacts?tenant_id=tenant-1&artifact_type=report");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var summaries = await listResponse.Content.ReadFromJsonAsync<List<ArtifactSummaryDto>>();
        Assert.NotNull(summaries);
        Assert.Contains(summaries!, s => s.ArtifactId == "artifact-1" && s.LatestVersion == "v1");

        var detailResponse = await client.GetAsync("/api/artifacts/artifact-1?tenant_id=tenant-1");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<ArtifactDetailDto>();
        Assert.NotNull(detail);
        Assert.Equal("artifact-1", detail!.ArtifactId);
        Assert.Single(detail.Versions);
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
