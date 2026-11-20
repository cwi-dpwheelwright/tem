using Cwi.Orchestrator.Application.Survival;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Survival;

public sealed class SurvivalEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsSurvivalWhenRecentCritical()
    {
        var registry = new SurvivalPolicyRegistry();
        var store = new SurvivalSignalStore();
        var evaluator = new SurvivalEvaluator(registry, store);

        store.Record(new SurvivalSignalDto
        {
            SignalType = "dependency",
            Severity = 0.95,
            ObservedUtc = DateTimeOffset.UtcNow
        });

        var status = evaluator.Evaluate();

        Assert.Equal("survival", status.Mode);
        Assert.Equal("critical", status.Profile);
    }

    [Fact]
    public void Evaluate_ReturnsNormalWhenExpired()
    {
        var registry = new SurvivalPolicyRegistry();
        var store = new SurvivalSignalStore();
        var evaluator = new SurvivalEvaluator(registry, store);

        store.Record(new SurvivalSignalDto
        {
            SignalType = "dependency",
            Severity = 0.95,
            ObservedUtc = DateTimeOffset.UtcNow.AddHours(-2)
        });

        var status = evaluator.Evaluate();

        Assert.Equal("normal", status.Mode);
    }

    [Fact]
    public void Evaluate_ReturnsDegradedWhenRecentDegraded()
    {
        var registry = new SurvivalPolicyRegistry();
        var store = new SurvivalSignalStore();
        var evaluator = new SurvivalEvaluator(registry, store);

        store.Record(new SurvivalSignalDto
        {
            SignalType = "dependency",
            Severity = 0.75,
            ObservedUtc = DateTimeOffset.UtcNow
        });

        var status = evaluator.Evaluate();

        Assert.Equal("degraded", status.Mode);
        Assert.Equal("degraded", status.Profile);
    }

    [Fact]
    public void Evaluate_UsesHysteresisForDegraded()
    {
        var registry = new SurvivalPolicyRegistry();
        var store = new SurvivalSignalStore();
        var evaluator = new SurvivalEvaluator(registry, store);

        store.Record(new SurvivalSignalDto
        {
            SignalType = "dependency",
            Severity = 0.75,
            ObservedUtc = DateTimeOffset.UtcNow
        });

        var degraded = evaluator.Evaluate();
        Assert.Equal("degraded", degraded.Mode);

        store.Record(new SurvivalSignalDto
        {
            SignalType = "dependency",
            Severity = 0.5,
            ObservedUtc = DateTimeOffset.UtcNow
        });

        var stillDegraded = evaluator.Evaluate();
        Assert.Equal("degraded", stillDegraded.Mode);

        store.Record(new SurvivalSignalDto
        {
            SignalType = "dependency",
            Severity = 0.35,
            ObservedUtc = DateTimeOffset.UtcNow
        });

        var recovered = evaluator.Evaluate();
        Assert.Equal("normal", recovered.Mode);
    }
}
