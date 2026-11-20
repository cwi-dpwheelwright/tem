using Cwi.Orchestrator.Application.BreakGlass;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.BreakGlass;

public class BreakGlassPolicyValidatorTests
{
    [Fact]
    public void Validate_rejects_missing_reason_when_required()
    {
        var policy = new BreakGlassPolicy
        {
            AllowedRoles = new[] { "admin" },
            AllowedScopes = new[] { "system" },
            MaxDurationMinutes = 30,
            RequireReason = true
        };
        var validator = new BreakGlassPolicyValidator(policy);
        var request = new BreakGlassRequest
        {
            Role = "admin",
            Scope = "system",
            DurationMinutes = 10,
            Reason = ""
        };

        var result = validator.Validate(request);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Validate_allows_valid_request()
    {
        var policy = new BreakGlassPolicy
        {
            AllowedRoles = new[] { "admin" },
            AllowedScopes = new[] { "system" },
            MaxDurationMinutes = 30,
            RequireReason = true
        };
        var validator = new BreakGlassPolicyValidator(policy);
        var request = new BreakGlassRequest
        {
            Role = "admin",
            Scope = "system",
            DurationMinutes = 10,
            Reason = "urgent"
        };

        var result = validator.Validate(request);

        result.IsSuccess.Should().BeTrue();
    }
}
