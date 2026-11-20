#nullable enable
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.DataAccess;
using Cwi.Orchestrator.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class ApprovalQueryTests
{
    private const string RolesHeader = "x-roles";
    private const string TenantHeader = "x-tenant-id";

    [Fact]
    public async Task GetStatus_ReturnsTicket()
    {
        var factory = CreateFactory("approval-status");
        using var client = factory.CreateClient();

        await SeedApprovalAsync(factory.Services, "tenant-a", "ticket-123");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/approval/ticket-123");
        request.Headers.Add(TenantHeader, "tenant-a");
        request.Headers.Add(RolesHeader, "approver");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ApprovalTicketStatusDto>();
        Assert.NotNull(payload);
        Assert.Equal("ticket-123", payload!.TicketId);
        Assert.Equal("pending", payload.Status);
    }

    [Fact]
    public async Task List_ReturnsTicketsForTenant()
    {
        var factory = CreateFactory("approval-list");
        using var client = factory.CreateClient();

        await SeedApprovalAsync(factory.Services, "tenant-list", "ticket-a");
        await SeedApprovalAsync(factory.Services, "tenant-list", "ticket-b");
        await SeedApprovalAsync(factory.Services, "tenant-other", "ticket-x");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/approval?tenant_id=tenant-list&limit=10");
        request.Headers.Add(TenantHeader, "tenant-list");
        request.Headers.Add(RolesHeader, "approver");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<List<ApprovalTicketStatusDto>>(json);
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Count);
    }

    [Fact]
    public async Task GetStatus_ReturnsForbidden_WhenRoleMissing()
    {
        var factory = CreateFactory("approval-status-forbidden");
        using var client = factory.CreateClient();

        await SeedApprovalAsync(factory.Services, "tenant-a", "ticket-403");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/approval/ticket-403");
        request.Headers.Add(TenantHeader, "tenant-a");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetStatus_ReturnsForbidden_WhenTenantMismatch()
    {
        var factory = CreateFactory("approval-status-tenant-mismatch");
        using var client = factory.CreateClient();

        await SeedApprovalAsync(factory.Services, "tenant-a", "ticket-tenant");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/approval/ticket-tenant");
        request.Headers.Add(TenantHeader, "tenant-b");
        request.Headers.Add(RolesHeader, "approver");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsForbidden_WhenTenantMissing()
    {
        var factory = CreateFactory("approval-list-forbidden");
        using var client = factory.CreateClient();

        await SeedApprovalAsync(factory.Services, "tenant-list", "ticket-a");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/approval?tenant_id=tenant-list&limit=10");
        request.Headers.Add(RolesHeader, "approver");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_RespectsLimit()
    {
        var factory = CreateFactory("approval-list-limit");
        using var client = factory.CreateClient();

        await SeedApprovalAsync(factory.Services, "tenant-list", "ticket-a");
        await SeedApprovalAsync(factory.Services, "tenant-list", "ticket-b");
        await SeedApprovalAsync(factory.Services, "tenant-list", "ticket-c");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp/approval?tenant_id=tenant-list&limit=1");
        request.Headers.Add(TenantHeader, "tenant-list");
        request.Headers.Add(RolesHeader, "approver");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<List<ApprovalTicketStatusDto>>(json);
        Assert.NotNull(payload);
        Assert.Single(payload!);
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

    private static async Task SeedApprovalAsync(IServiceProvider services, string tenantId, string ticketId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        db.ApprovalTickets.Add(new ApprovalTicket
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            TenantId = tenantId,
            UserId = "user",
            Risk = "medium",
            Reason = "seeded",
            Status = "pending",
            IdempotencyKey = $"{tenantId}:{ticketId}",
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        });
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
