using Cwi.Orchestrator.Application.Reconciliation;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Reconciliation;

public sealed class RecordReconcilerTests
{
    [Fact]
    public void BuildCanonical_PrefersLatestSource()
    {
        var reconciler = new RecordReconciler();
        var older = new SourceRecordDto
        {
            SourceId = "old",
            UpdatedUtc = DateTimeOffset.UtcNow.AddHours(-1),
            Attributes = new Dictionary<string, object?> { { "status", "open" } }
        };
        var newer = new SourceRecordDto
        {
            SourceId = "new",
            UpdatedUtc = DateTimeOffset.UtcNow,
            Attributes = new Dictionary<string, object?> { { "status", "closed" }, { "priority", "high" } }
        };

        var canonical = reconciler.BuildCanonical(new[] { older, newer });

        Assert.Equal("closed", canonical["status"]);
        Assert.Equal("high", canonical["priority"]);
    }
}
