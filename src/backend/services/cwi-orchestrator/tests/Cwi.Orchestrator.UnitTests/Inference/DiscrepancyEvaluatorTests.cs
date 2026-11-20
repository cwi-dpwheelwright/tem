#nullable enable
using System.Collections.Generic;
using Cwi.Orchestrator.Application.Inference;
using Cwi.Orchestrator.Application.Retrieval;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Inference;

public class DiscrepancyEvaluatorTests
{
    [Fact]
    public void Evaluate_detects_conflicting_sources()
    {
        var evaluator = new DiscrepancyEvaluator();
        var sources = new List<RetrievalResult>
        {
            new("doc-a", "chunk-1", "Dose is 10 mg.", 0.9f),
            new("doc-b", "chunk-2", "Dose is 100 mg.", 0.8f)
        };

        var result = evaluator.Evaluate("Dose is 10 mg.", sources);

        result.Discrepancies.Should().NotBeEmpty();
        result.Severity.Should().NotBe("low");
        result.Discrepancies.Should().Contain(d => d.Category == "numeric");
    }

    [Fact]
    public void Evaluate_suggests_correction_when_answer_diverges()
    {
        var evaluator = new DiscrepancyEvaluator();
        var sources = new List<RetrievalResult>
        {
            new("doc-a", "chunk-1", "Length is 2 cm.", 0.9f),
            new("doc-b", "chunk-2", "Length is 3 cm.", 0.8f)
        };

        var result = evaluator.Evaluate("Length is 10 cm.", sources);

        result.Corrections.Should().NotBeEmpty();
        result.Discrepancies.Should().NotBeEmpty();
    }

    [Fact]
    public void Evaluate_detects_logical_contradictions()
    {
        var evaluator = new DiscrepancyEvaluator();
        var sources = new List<RetrievalResult>
        {
            new("doc-a", "chunk-1", "Feature is enabled.", 0.9f),
            new("doc-b", "chunk-2", "Feature is disabled.", 0.8f)
        };

        var result = evaluator.Evaluate("Feature is enabled.", sources);

        result.Discrepancies.Should().Contain(d => d.Category == "logical");
        result.Severity.Should().Be("high");
    }
}
#nullable disable
