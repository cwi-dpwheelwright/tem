using System.Net;
using System.Net.Http.Json;
using Cwi.Orchestrator.Api.Auth;
using Cwi.Orchestrator.Application.Reconciliation;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.DataAccess;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class ReconciliationControllerTests
{
    [Fact]
    public async Task UpsertAndGet_ReturnsCanonicalRecord()
    {
        var factory = CreateFactory("recon-db");
        using var client = factory.CreateClient();

        var request = new CanonicalRecordUpsertRequestDto
        {
            RecordId = "record-1",
            TenantId = "tenant-1",
            UserId = "user-1",
            Sources = new List<SourceRecordDto>
            {
                new()
                {
                    SourceId = "source-a",
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    Attributes = new Dictionary<string, object?> { { "status", "open" } }
                },
                new()
                {
                    SourceId = "source-b",
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    Attributes = new Dictionary<string, object?> { { "status", "closed" } }
                }
            }
        };

        var upsert = await client.PostAsJsonAsync("/api/reconciliation/records", request);
        Assert.Equal(HttpStatusCode.OK, upsert.StatusCode);

        var get = await client.GetAsync("/api/reconciliation/records/record-1");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var payload = await get.Content.ReadFromJsonAsync<CanonicalRecordResponseDto>();
        Assert.NotNull(payload);
        Assert.Equal("record-1", payload!.RecordId);
        Assert.NotNull(payload.ApprovalTicket);

        var report = await client.GetAsync("/api/reconciliation/records/record-1/report");
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);
        var reportPayload = await report.Content.ReadFromJsonAsync<ReconciliationReportDto>();
        Assert.NotNull(reportPayload);
        Assert.NotNull(reportPayload!.ApprovalTicket);
    }


    [Fact]
    public async Task ApprovalComplete_UpdatesRecordAndLedger()
    {
        var factory = CreateFactory("recon-approval");
        using var client = factory.CreateClient();

        var recordId = "record-approval";
        var request = new CanonicalRecordUpsertRequestDto
        {
            RecordId = recordId,
            TenantId = "tenant-1",
            UserId = "user-1",
            Sources = new List<SourceRecordDto>
            {
                new()
                {
                    SourceId = "source-a",
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    Attributes = new Dictionary<string, object?> { { "status", "open" } }
                },
                new()
                {
                    SourceId = "source-b",
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    Attributes = new Dictionary<string, object?> { { "status", "closed" } }
                }
            }
        };

        var upsert = await client.PostAsJsonAsync("/api/reconciliation/records", request);
        Assert.Equal(HttpStatusCode.OK, upsert.StatusCode);
        var upsertPayload = await upsert.Content.ReadFromJsonAsync<CanonicalRecordResponseDto>();
        Assert.NotNull(upsertPayload);
        Assert.NotNull(upsertPayload!.ApprovalTicket);

        var approvalRequest = new ApprovalCompleteRequestDto
        {
            TicketId = upsertPayload.ApprovalTicket!.TicketId,
            Decision = "approve",
            ApproverId = "approver-1",
            Reason = "test"
        };

        var approval = await client.PostAsJsonAsync($"/api/reconciliation/records/{recordId}/approval/complete", approvalRequest);
        Assert.Equal(HttpStatusCode.OK, approval.StatusCode);

        var get = await client.GetAsync($"/api/reconciliation/records/{recordId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var payload = await get.Content.ReadFromJsonAsync<CanonicalRecordResponseDto>();
        Assert.NotNull(payload);
        Assert.Empty(payload!.Conflicts);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
        var ledgerEvent = await db.LedgerEvents
            .FirstOrDefaultAsync(evt => evt.TraceId == recordId && evt.EventType == "reconciliation.approval_decision");
        Assert.NotNull(ledgerEvent);
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
