using Cwi.Router.Application.Routing;
using FluentAssertions;
using Xunit;

namespace Cwi.Router.UnitTests;

public sealed class RouteAttestationValidatorTests
{
    [Fact]
    public void RejectsDuplicateNonce()
    {
        var store = new RouteAttestationReplayStore();
        var validator = new RouteAttestationValidator(store);
        var envelope = new RouteAttestationEnvelope
        {
            TraceId = "trace-1",
            DecisionId = "decision-1",
            SelectedTier = "pro",
            Nonce = "nonce-123",
            Sequence = 1
        };

        validator.TryValidate(envelope, out _).Should().BeTrue();
        validator.TryValidate(envelope, out var reason).Should().BeFalse();
        reason.Should().BeOneOf("nonce_replay", "sequence_replay");
    }

    [Fact]
    public void RejectsOutOfOrderSequence()
    {
        var store = new RouteAttestationReplayStore();
        var validator = new RouteAttestationValidator(store);

        var first = new RouteAttestationEnvelope
        {
            TraceId = "trace-1",
            DecisionId = "decision-1",
            SelectedTier = "pro",
            Nonce = "nonce-1",
            Sequence = 2
        };

        var second = new RouteAttestationEnvelope
        {
            TraceId = "trace-1",
            DecisionId = "decision-1",
            SelectedTier = "pro",
            Nonce = "nonce-2",
            Sequence = 1
        };

        validator.TryValidate(first, out _).Should().BeTrue();
        validator.TryValidate(second, out var reason).Should().BeFalse();
        reason.Should().Be("sequence_replay");
    }
}
