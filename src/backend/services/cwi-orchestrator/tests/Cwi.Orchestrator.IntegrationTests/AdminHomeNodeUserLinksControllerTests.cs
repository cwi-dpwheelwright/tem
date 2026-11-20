using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.DataAccess;
using Cwi.Orchestrator.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class AdminHomeNodeUserLinksControllerTests
{
    [Fact]
    public async Task List_returns_active_link_rows_with_node_details_for_explicit_admin_entitlement()
    {
        var factory = CreateFactory($"home-node-user-links-{Guid.NewGuid():N}");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken("tenant-a", "platform_admin", cwiAdmin: true));

        using (var seedScope = factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
            var now = DateTimeOffset.UtcNow;
            db.HomeNodeRecords.Add(new HomeNodeRecord
            {
                Id = Guid.NewGuid(),
                TenantId = "CWI-ops",
                NodeId = "home-node-1",
                OwnerUserId = "user-1",
                TailscaleIp = "100.64.0.10",
                Status = "active",
                LastHeartbeatUtc = now,
                CreatedUtc = now,
                UpdatedUtc = now
            });
            db.HomeNodeAssignments.Add(new HomeNodeAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = "CWI-ops",
                UserId = "user-1",
                PrimaryHomeNodeId = "home-node-1",
                LocalOnly = true,
                RemoteFallbackAllowed = false,
                Status = "active",
                AssignedUtc = now.AddMinutes(-5),
                UpdatedUtc = now
            });
            db.HomeNodeAssignments.Add(new HomeNodeAssignment
            {
                Id = Guid.NewGuid(),
                TenantId = "CWI-ops",
                UserId = "user-2",
                PrimaryHomeNodeId = "home-node-2",
                LocalOnly = true,
                RemoteFallbackAllowed = false,
                Status = "revoked",
                AssignedUtc = now.AddMinutes(-10),
                RevokedUtc = now.AddMinutes(-1),
                UpdatedUtc = now
            });
            await db.SaveChangesAsync();
        }

        var response = await client.GetAsync("/api/admin/home-node-user-links?tenant_id=CWI-ops");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var links = await response.Content.ReadFromJsonAsync<List<HomeNodeUserLinkResponse>>();
        Assert.NotNull(links);
        var link = Assert.Single(links!);
        Assert.Equal("CWI-ops", link.TenantId);
        Assert.Equal("user-1", link.UserId);
        Assert.True(link.Linked);
        Assert.Equal("home-node-1", link.NodeId);
        Assert.Equal("active", link.NodeStatus);
        Assert.Equal("100.64.0.10", link.TailscaleIp);
    }

    [Fact]
    public async Task List_rejects_non_admin_cross_tenant_token()
    {
        var factory = CreateFactory($"home-node-user-links-deny-{Guid.NewGuid():N}");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken("tenant-a", role: null));

        var response = await client.GetAsync("/api/admin/home-node-user-links?tenant_id=CWI-ops");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_rejects_legacy_admin_tenant_role_without_admin_entitlement()
    {
        var factory = CreateFactory($"home-node-user-links-legacy-deny-{Guid.NewGuid():N}");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken("admin", "admin", cwiAdmin: false));

        var response = await client.GetAsync("/api/admin/home-node-user-links?tenant_id=CWI-ops");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_allows_explicit_admin_entitlement_from_any_tenant()
    {
        var factory = CreateFactory($"home-node-user-links-platform-admin-{Guid.NewGuid():N}");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken("tenant-a", "platform_admin", cwiAdmin: true));

        var response = await client.GetAsync("/api/admin/home-node-user-links?tenant_id=CWI-ops");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
                    services.AddSingleton(new AuthOptions
                    {
                        AuthRequired = true,
                        AllowInsecureJwt = true,
                        AdminTenantId = "admin"
                    });
                    services.RemoveAll(typeof(DbContextOptions<OrchestratorDbContext>));
                    services.RemoveAll(typeof(OrchestratorDbContext));
                    services.AddDbContext<OrchestratorDbContext>(options =>
                    {
                        options.UseInMemoryDatabase(dbName);
                    });
                });
            });
    }

    private static string CreateToken(string tenantId, string? role, bool cwiAdmin = false, bool isSuperAdmin = false)
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("tenant_id", tenantId),
            new("is_superadmin", isSuperAdmin ? "true" : "false"),
            new("cwi_admin", cwiAdmin ? "true" : "false")
        };
        if (!string.IsNullOrWhiteSpace(role))
        {
            claims.Add(new Claim("role", role));
        }

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5)));
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

    private sealed record HomeNodeUserLinkResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("tenant_id")] string TenantId,
        [property: System.Text.Json.Serialization.JsonPropertyName("user_id")] string UserId,
        [property: System.Text.Json.Serialization.JsonPropertyName("linked")] bool Linked,
        [property: System.Text.Json.Serialization.JsonPropertyName("node_id")] string? NodeId,
        [property: System.Text.Json.Serialization.JsonPropertyName("node_status")] string? NodeStatus,
        [property: System.Text.Json.Serialization.JsonPropertyName("tailscale_ip")] string? TailscaleIp);
}
