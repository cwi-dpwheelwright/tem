using Cwi.Orchestrator.Application.Guardrails.Config;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Guardrails;

public class GuardrailConfigLoaderTests
{
    [Fact]
    public void Load_accepts_valid_config()
    {
        const string json = """
                            {
                              "version": 1,
                              "validators": [
                                {
                                  "id": "quantity_bounds",
                                  "settings": {
                                    "unit": "mg",
                                    "min": "1",
                                    "max": "10"
                                  }
                                }
                              ]
                            }
                            """;

        var loader = new GuardrailConfigLoader();
        var result = loader.Load(json);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Validators.Should().HaveCount(1);
        result.Data.Validators[0].Id.Should().Be("quantity_bounds");
    }

    [Fact]
    public void Load_rejects_missing_validators()
    {
        const string json = """
                            {
                              "version": 1
                            }
                            """;

        var loader = new GuardrailConfigLoader();
        var result = loader.Load(json);

        result.IsSuccess.Should().BeFalse();
    }
}
