using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.DataAccess;
using Cwi.Orchestrator.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class TrustedWebAdminControllerTests
{
    [Fact]
    public async Task Put_then_get_persists_trusted_web_config_in_global_config()
    {
        var factory = CreateFactory($"trusted-web-admin-{Guid.NewGuid():N}");
        using var client = factory.CreateClient();
        using (var seedScope = factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
            db.TenantConfigs.Add(new TenantConfig
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-smoke",
                ConfigJson = """{"policy_overrides":{"mode":"observe"}}""",
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
            db.TenantConfigs.Add(new TenantConfig
            {
                Id = Guid.NewGuid(),
                TenantId = "__global_trusted_web__",
                ConfigJson = """{"policy_overrides":{"mode":"observe"}}""",
                CreatedUtc = DateTimeOffset.UtcNow,
                UpdatedUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var payload = new
        {
            enabled = true,
            version = 1,
            default_profile = "general",
            profiles = new
            {
                general = new
                {
                    mode = "trusted_sites_only",
                    max_results = 2,
                    sources = new[]
                    {
                        new
                        {
                            source_id = "trusted-docs",
                            display_name = "Trusted Docs",
                            allowed_domains = new[] { "https://Docs.Example.com/" },
                            seed_urls = new[] { "https://docs.example.com/current" },
                            sitemap_urls = Array.Empty<string>(),
                            rss_urls = Array.Empty<string>(),
                            include_path_prefixes = new[] { "/current" },
                            exclude_path_prefixes = Array.Empty<string>()
                        }
                    }
                }
            }
        };

        var put = await client.PutAsJsonAsync("/api/admin/trusted-web", payload);

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        using (var putJson = JsonDocument.Parse(await put.Content.ReadAsStringAsync()))
        {
            var root = putJson.RootElement;
            Assert.True(root.GetProperty("enabled").GetBoolean());
            Assert.Equal("general", root.GetProperty("default_profile").GetString());
            var source = root.GetProperty("profiles").GetProperty("general").GetProperty("sources")[0];
            Assert.Equal("docs.example.com", source.GetProperty("allowed_domains")[0].GetString());
        }

        var get = await client.GetAsync("/api/admin/trusted-web");

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using (var getJson = JsonDocument.Parse(await get.Content.ReadAsStringAsync()))
        {
            var root = getJson.RootElement;
            Assert.True(root.GetProperty("enabled").GetBoolean());
            Assert.Equal("trusted_sites_only", root.GetProperty("profiles").GetProperty("general").GetProperty("mode").GetString());
            Assert.Equal("https://docs.example.com/current", root.GetProperty("profiles").GetProperty("general").GetProperty("sources")[0].GetProperty("seed_urls")[0].GetString());
        }

        using var scope = factory.Services.CreateScope();
        var verificationDb = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var record = await verificationDb.TenantConfigs.SingleAsync(config => config.TenantId == "__global_trusted_web__");
        Assert.Contains("\"trusted_web_context\"", record.ConfigJson);
        Assert.Contains("\"policy_overrides\"", record.ConfigJson);

        var tenantRecord = await verificationDb.TenantConfigs.SingleAsync(config => config.TenantId == "tenant-smoke");
        Assert.DoesNotContain("\"trusted_web_context\"", tenantRecord.ConfigJson);
    }

    private static WebApplicationFactory<Program> CreateFactory(string dbName)
    {
        ConfigureEnvironment();

        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll(typeof(AuthOptions));
                    services.AddSingleton(new AuthOptions { AuthRequired = false });
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
        Environment.SetEnvironmentVariable("PACKS_BASE_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("ORCHESTRATOR_DB_CONNECTION", "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test");
        Environment.SetEnvironmentVariable("CWI_GRAPH_DB_DSN", "Host=127.0.0.1;Port=1;Database=test;Username=test;Password=test");
        Environment.SetEnvironmentVariable("VECTOR_SHARD_1_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("VECTOR_SHARD_2_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("VECTOR_EMBEDDING_DIMENSION", "64");
        Environment.SetEnvironmentVariable("MODEL_BASE_URL", "http://127.0.0.1:1");
        Environment.SetEnvironmentVariable("MODEL_NAME", "llama3");
        Environment.SetEnvironmentVariable("MODEL_TIMEOUT_SECONDS", "1");
    }
}
