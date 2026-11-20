using System;
using System.IO;
using Cwi.Orchestrator.Application.Recursive;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Recursive;

public sealed class RecursiveEventSchemaValidatorTests
{
    [Fact]
    public void ValidateJson_accepts_valid_recursive_event_fixture()
    {
        var validator = new RecursiveEventSchemaValidator();
        var json = File.ReadAllText(ResolveFixture("recursive-event.valid.json"));

        var result = validator.ValidateJson(json);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void ValidateJson_rejects_invalid_recursive_event_fixture()
    {
        var validator = new RecursiveEventSchemaValidator();
        var json = File.ReadAllText(ResolveFixture("recursive-event.invalid-missing-tenant.json"));

        var result = validator.ValidateJson(json);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Theory]
    [InlineData("recursive-event.duplicate.json")]
    [InlineData("recursive-event.stale.json")]
    public void ValidateJson_accepts_duplicate_and_stale_fixtures_for_ingestion_policy_tests(string fixtureName)
    {
        var validator = new RecursiveEventSchemaValidator();
        var json = File.ReadAllText(ResolveFixture(fixtureName));

        var result = validator.ValidateJson(json);

        result.IsSuccess.Should().BeTrue();
    }

    private static string ResolveFixture(string fixtureName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "contracts",
                "fixtures",
                "orchestrator",
                fixtureName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find recursive event fixture {fixtureName} from {AppContext.BaseDirectory}.");
    }
}
