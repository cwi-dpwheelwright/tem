using System.Text.Json;
using Cwi.Orchestrator.Application.Guardrails.Parsing;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Guardrails;

public sealed class QuantityExtractorFixtureTests
{
    [Fact]
    public void Extract_matches_fixture_cases()
    {
        var fixturePath = Path.Combine(ResolveFixtureRoot(), "parsed-outputs.json");
        var json = File.ReadAllText(fixturePath);
        var fixture = JsonSerializer.Deserialize<ParsedOutputFixture>(json);

        fixture.Should().NotBeNull();

        foreach (var testCase in fixture!.Cases)
        {
            var quantities = QuantityExtractor.Extract(testCase.Input);
            quantities.Count.Should().Be(testCase.Expected.Count);

            for (var index = 0; index < testCase.Expected.Count; index++)
            {
                quantities[index].Value.Should().BeApproximately(testCase.Expected[index].Value, 0.001);
                quantities[index].Unit.Should().Be(testCase.Expected[index].Unit);
            }
        }
    }

    private static string ResolveFixtureRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++)
        {
            var candidate = Path.Combine(current.FullName, "contracts", "fixtures", "orchestrator");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate contracts/fixtures/orchestrator.");
    }

    private sealed class ParsedOutputFixture
    {
        public List<ParsedOutputCase> Cases { get; init; } = new();
    }

    private sealed class ParsedOutputCase
    {
        public string Input { get; init; } = string.Empty;
        public List<ParsedOutputExpectation> Expected { get; init; } = new();
    }

    private sealed class ParsedOutputExpectation
    {
        public double Value { get; init; }
        public string Unit { get; init; } = string.Empty;
    }
}
