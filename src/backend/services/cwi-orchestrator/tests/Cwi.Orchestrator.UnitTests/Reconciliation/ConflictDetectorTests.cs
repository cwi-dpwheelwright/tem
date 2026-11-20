using Cwi.Orchestrator.Application.Reconciliation;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Reconciliation;

public sealed class ConflictDetectorTests
{
    [Fact]
    public void DetectConflicts_FindsDivergentValues()
    {
        var detector = new ConflictDetector();
        var sources = new[]
        {
            new SourceRecordDto
            {
                SourceId = "a",
                UpdatedUtc = DateTimeOffset.UtcNow,
                Attributes = new Dictionary<string, object?> { { "status", "open" } }
            },
            new SourceRecordDto
            {
                SourceId = "b",
                UpdatedUtc = DateTimeOffset.UtcNow,
                Attributes = new Dictionary<string, object?> { { "status", "closed" } }
            }
        };

        var conflicts = detector.DetectConflicts(sources);

        Assert.Single(conflicts);
        Assert.Equal("status", conflicts[0].Field);
        Assert.Equal(2, conflicts[0].Values.Count);
    }
}
