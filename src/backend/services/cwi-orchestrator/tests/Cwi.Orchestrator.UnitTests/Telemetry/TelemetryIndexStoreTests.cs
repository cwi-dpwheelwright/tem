using Cwi.Orchestrator.Application.Telemetry;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Telemetry;

public sealed class TelemetryIndexStoreTests
{
    [Fact]
    public void Record_DoesNothing_WhenDisabled()
    {
        var options = new TelemetryIndexOptions { Enabled = false, MaxEntries = 10 };
        var store = new TelemetryIndexStore(options);

        store.Record(new TelemetryIndexEntry
        {
            TenantId = "tenant",
            IngestId = "ingest-1",
            CreatedUtc = DateTimeOffset.UtcNow
        });

        store.GetLatest(null, 10).Should().BeEmpty();
    }

    [Fact]
    public void Record_Trims_WhenExceedingMaxEntries()
    {
        var options = new TelemetryIndexOptions { Enabled = true, MaxEntries = 2 };
        var store = new TelemetryIndexStore(options);

        store.Record(new TelemetryIndexEntry
        {
            TenantId = "tenant",
            IngestId = "ingest-1",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-3)
        });
        store.Record(new TelemetryIndexEntry
        {
            TenantId = "tenant",
            IngestId = "ingest-2",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
        });
        store.Record(new TelemetryIndexEntry
        {
            TenantId = "tenant",
            IngestId = "ingest-3",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        var items = store.GetLatest("tenant", 10);
        items.Should().HaveCount(2);
        items[0].IngestId.Should().Be("ingest-3");
        items[1].IngestId.Should().Be("ingest-2");
    }
}
