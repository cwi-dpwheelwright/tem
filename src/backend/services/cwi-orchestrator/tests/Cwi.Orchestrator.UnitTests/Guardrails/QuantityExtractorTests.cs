using Cwi.Orchestrator.Application.Guardrails.Parsing;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Guardrails;

public class QuantityExtractorTests
{
    [Fact]
    public void Extract_returns_quantities_with_supported_units()
    {
        var quantities = QuantityExtractor.Extract("Dose is 10 mg and length 2 cm.");

        quantities.Should().HaveCount(2);
        quantities[0].Unit.Should().Be("mg");
        quantities[1].Unit.Should().Be("cm");
    }
}
