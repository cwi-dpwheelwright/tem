using Cwi.Orchestrator.Application.Guardrails.Units;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Guardrails;

public class UnitNormalizerTests
{
    [Theory]
    [InlineData("10 mg", 1e-5)]
    [InlineData("2 g", 0.002)]
    [InlineData("1 kg", 1.0)]
    public void Normalize_mass_to_kg(string input, double expectedKg)
    {
        UnitNormalizer.TryParseQuantity(input, out var quantity, out var error).Should().BeTrue(error);
        var result = UnitNormalizer.Normalize(quantity);

        result.Success.Should().BeTrue();
        result.Normalized.Should().NotBeNull();
        result.Normalized!.Value.Unit.Should().Be("kg");
        result.Normalized.Value.Dimension.Should().Be(Dimension.Mass);
        result.Normalized.Value.Value.Should().BeApproximately(expectedKg, 1e-12);
    }

    [Theory]
    [InlineData("0 C", 273.15)]
    [InlineData("32 F", 273.15)]
    [InlineData("300 K", 300.0)]
    public void Normalize_temperature_to_kelvin(string input, double expectedK)
    {
        UnitNormalizer.TryParseQuantity(input, out var quantity, out var error).Should().BeTrue(error);
        var result = UnitNormalizer.Normalize(quantity);

        result.Success.Should().BeTrue();
        result.Normalized.Should().NotBeNull();
        result.Normalized!.Value.Unit.Should().Be("K");
        result.Normalized.Value.Dimension.Should().Be(Dimension.Temperature);
        result.Normalized.Value.Value.Should().BeApproximately(expectedK, 1e-2);
    }

    [Fact]
    public void AreCompatible_returns_false_for_unknown_units()
    {
        UnitNormalizer.AreCompatible(new Quantity(1, "m"), new Quantity(1, "unknown")).Should().BeFalse();
    }

    [Fact]
    public void AreCompatible_returns_true_for_same_dimension()
    {
        UnitNormalizer.AreCompatible(new Quantity(1, "cm"), new Quantity(2, "m")).Should().BeTrue();
    }

    [Fact]
    public void Normalize_returns_error_for_unknown_unit()
    {
        var result = UnitNormalizer.Normalize(new Quantity(1, "unknown"));
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Unsupported unit");
    }
}
