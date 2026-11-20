using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.DataAccess;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class RecursiveStructuralControllerTests
{
    [Fact]
    public async Task Recursive_event_ingest_creates_queryable_graph_candidate_and_vector_refresh_job()
    {
        var factory = CreateFactory($"recursive-structural-{Guid.NewGuid():N}");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateAdminToken());

        using var content = new StringContent(
            File.ReadAllText(ResolveFixture("recursive-event.stale.json")),
            Encoding.UTF8,
            "application/json");

        var ingestResponse = await client.PostAsync("/api/recursive/events", content);

        Assert.Equal(HttpStatusCode.Created, ingestResponse.StatusCode);
        var ingest = await ingestResponse.Content.ReadFromJsonAsync<RecursiveEventIngestResponseDto>();
        Assert.NotNull(ingest);
        Assert.True(ingest!.Accepted);
        Assert.False(ingest.Quarantined);
        Assert.Equal("rec-evt-stale-0001", ingest.EventId);
        Assert.Equal("stale", ingest.TemporalState);

        var events = await client.GetFromJsonAsync<List<RecursiveEventRecordDto>>(
            "/api/recursive/events?tenant_id=tenant-1&event_type=memory_candidate.observed&temporal_state=stale");
        Assert.NotNull(events);
        var recursiveEvent = Assert.Single(events!);
        Assert.Equal("rec-evt-stale-0001", recursiveEvent.EventId);
        Assert.Equal("trace-stale-001", recursiveEvent.TraceId);

        var candidates = await client.GetFromJsonAsync<List<GraphUpdateCandidateDto>>(
            "/api/admin/graph-update-candidates?tenant_id=tenant-1&candidate_state=add&temporal_state=stale");
        Assert.NotNull(candidates);
        var candidate = Assert.Single(candidates!);
        Assert.Equal("rec-evt-stale-0001", candidate.RecursiveEventId);
        Assert.Equal("trace-stale-001", candidate.TraceId);
        Assert.Equal("project_priority", candidate.Subject);
        Assert.Equal("observed", candidate.Relation);

        var jobs = await client.GetFromJsonAsync<List<GraphVectorRefreshJobDto>>(
            $"/api/admin/graph-vector-refresh-jobs?tenant_id=tenant-1&refresh_status=pending&graph_candidate_id={candidate.CandidateId}");
        Assert.NotNull(jobs);
        var job = Assert.Single(jobs!);
        Assert.Equal(candidate.CandidateId, job.GraphCandidateId);
        Assert.Equal("rec-evt-stale-0001", job.RecursiveEventId);
        Assert.Equal("pending", job.RefreshStatus);
        Assert.Equal("graph_candidate_add_stale", job.RefreshReason);
    }

    [Fact]
    public async Task Recursive_event_ingest_requires_admin_entitlement()
    {
        var factory = CreateFactory($"recursive-structural-deny-{Guid.NewGuid():N}");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(cwiAdmin: false));

        using var content = new StringContent(
            File.ReadAllText(ResolveFixture("recursive-event.stale.json")),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/recursive/events", content);

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

    private static string CreateAdminToken()
    {
        return CreateToken(cwiAdmin: true);
    }

    private static string CreateToken(bool cwiAdmin)
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("tenant_id", "tenant-1"),
            new("cwi_admin", cwiAdmin ? "true" : "false")
        };

        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5)));
    }

    private static string ResolveFixture(string fixtureName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "contracts",
                "fixtures",
                "orchestrator",
                fixtureName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find recursive event fixture {fixtureName} from {AppContext.BaseDirectory}.");
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
