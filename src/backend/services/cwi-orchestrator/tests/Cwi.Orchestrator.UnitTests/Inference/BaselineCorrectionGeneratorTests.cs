#nullable enable
using System.Collections.Generic;
using Cwi.Orchestrator.Application.Inference;
using Cwi.Orchestrator.Application.Models;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Inference;

public class BaselineCorrectionGeneratorTests
{
    [Fact]
    public void Generate_applies_simple_replacements()
    {
        var generator = new BaselineCorrectionGenerator();
        var evaluation = new DiscrepancyEvaluation
        {
            Corrections = new List<CorrectionDiff>
            {
                new()
                {
                    Field = "length",
                    From = "10 cm",
                    To = "2 cm",
                    Note = "Adjust to median"
                }
            }
        };

        var plan = generator.Generate("Length is 10 cm.", evaluation);

        plan.CorrectedAnswer.Should().Be("Length is 2 cm.");
        plan.Corrections.Should().HaveCount(1);
    }
}
#nullable disable
