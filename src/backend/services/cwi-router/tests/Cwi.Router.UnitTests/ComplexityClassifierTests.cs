using Cwi.Router.Application.Routing;
using FluentAssertions;
using Xunit;

namespace Cwi.Router.UnitTests;

public sealed class ComplexityClassifierTests
{
    [Fact]
    public void AskPayload_ClassifiesLowComplexity()
    {
        var classifier = new ComplexityClassifier();
        var body = "{\"question\":\"Hello world\",\"context\":{\"top_k\":2}}";

        var ok = classifier.TryClassify("/api/ask", body, out var descriptor);

        ok.Should().BeTrue();
        descriptor.RequestKind.Should().Be("ask");
        descriptor.WordCount.Should().Be(2);
        descriptor.Level.Should().Be(ComplexityLevel.Low);
    }

    [Fact]
    public void ActionPayload_ClassifiesHighWhenRiskExplicit()
    {
        var classifier = new ComplexityClassifier();
        var body = "{\"action\":{\"type\":\"risk_demo\",\"payload\":{\"risk\":\"high\"}}}";

        var ok = classifier.TryClassify("/api/action", body, out var descriptor);

        ok.Should().BeTrue();
        descriptor.RequestKind.Should().Be("action");
        descriptor.ActionType.Should().Be("risk_demo");
        descriptor.Level.Should().Be(ComplexityLevel.High);
    }

    [Fact]
    public void IgnoresNonTargetPaths()
    {
        var classifier = new ComplexityClassifier();
        var ok = classifier.TryClassify("/api/health", "{}", out _);

        ok.Should().BeFalse();
    }
}
