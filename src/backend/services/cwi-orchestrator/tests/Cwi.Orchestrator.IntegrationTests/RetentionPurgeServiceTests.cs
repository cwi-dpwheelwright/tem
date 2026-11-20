using System;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Retention;
using Cwi.Orchestrator.Composition.Retention;
using Cwi.Orchestrator.DataAccess;
using Cwi.Orchestrator.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Cwi.Orchestrator.IntegrationTests;

public sealed class RetentionPurgeServiceTests
{
    [Fact]
    public async Task PurgeAsync_RemovesExpiredLedgerAndTelemetry()
    {
        var options = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseInMemoryDatabase("retention-purge")
            .Options;

        await using var dbContext = new OrchestratorDbContext(options);
        var now = DateTimeOffset.UtcNow;

        dbContext.LedgerEvents.AddRange(
            new LedgerEvent
            {
                Id = Guid.NewGuid(),
                TraceId = "trace-old",
                EventType = "old",
                PayloadJson = "{}",
                PrevHash = "",
                Hash = "hash-old",
                HashAlgorithm = "sha256",
                CreatedUtc = now.AddDays(-10)
            },
            new LedgerEvent
            {
                Id = Guid.NewGuid(),
                TraceId = "trace-new",
                EventType = "new",
                PayloadJson = "{}",
                PrevHash = "",
                Hash = "hash-new",
                HashAlgorithm = "sha256",
                CreatedUtc = now.AddDays(-1)
            });

        dbContext.TelemetryWindows.AddRange(
            new TelemetryWindow
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-a",
                WindowStartUtc = now.AddMinutes(-15),
                WindowEndUtc = now.AddMinutes(-10),
                PayloadJson = "{}",
                CreatedUtc = now.AddDays(-9)
            },
            new TelemetryWindow
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-a",
                WindowStartUtc = now.AddMinutes(-5),
                WindowEndUtc = now,
                PayloadJson = "{}",
                CreatedUtc = now.AddDays(-1)
            });

        await dbContext.SaveChangesAsync();

        var retentionOptions = new RetentionPolicyOptions
        {
            LedgerDays = 5,
            TelemetryDays = 7,
            TraceDays = 0,
            PurgeEnabled = true
        };

        var logger = LoggerFactory.Create(builder => builder.AddFilter(_ => false))
            .CreateLogger<RetentionPurgeService>();
        var service = new RetentionPurgeService(dbContext, retentionOptions, logger);

        var result = await service.PurgeAsync(CancellationToken.None);

        Assert.Equal(1, result.LedgerRemoved);
        Assert.Equal(1, result.TelemetryRemoved);
        Assert.Equal(1, await dbContext.LedgerEvents.CountAsync());
        Assert.Equal(1, await dbContext.TelemetryWindows.CountAsync());
    }
}
