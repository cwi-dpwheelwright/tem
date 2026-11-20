using Cwi.Common;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Trace;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskNormalizationVerificationRouteStepTests
{
    [Fact]
    public async Task ExecuteAsync_asks_did_you_mean_when_normalization_requires_verification()
    {
        var step = new AskNormalizationVerificationRouteStep(
            JourneyTestHelpers.CreateContext(),
            new TraceLookupStore());
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "capitol texas",
            "enterprise",
            null,
            "observe",
            3,
            normalization: LanguageNormalizer.Normalize("capitol texas"));
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeFalse();
        response.Data!.Answer.Should().Be("Did you mean \"capital of Texas\" or \"Texas State Capitol\"?");
        response.Data.Trace.Route.Should().Be("clarification_handler");
        response.Data.Trace.QueryClassification.Should().Be("clarification_required");
        response.Data.Trace.IntentType.Should().Be("needs_clarification");
        response.Data.Trace.RetrievalSkippedReason.Should().Be("normalization_requires_verification");
        response.Data.Trace.NormalizationRequiresVerification.Should().BeTrue();
        response.Data.Trace.NormalizationConfidence.Should().BeLessThan(AskWorkflowContext.NormalizationContinuationConfidenceThreshold);
        response.Data.Trace.NormalizationConfidenceThreshold.Should().Be(AskWorkflowContext.NormalizationContinuationConfidenceThreshold);
        response.Data.Trace.NormalizationVerificationMessage.Should().Be("Did you mean \"capital of Texas\" or \"Texas State Capitol\"?");
        response.Data.Trace.NormalizationVerificationCandidates.Should().BeEquivalentTo("capital of Texas", "Texas State Capitol");
        response.Data.Trace.NormalizationCorrectionDecision.Should().Be("verification_required");
        response.Data.Trace.NormalizationCorrectionRisk.Should().Be("ambiguous_meaning");
        response.Data.Trace.NormalizationCorrectionVisibility.Should().Be("clarify");
        response.Data.Trace.NormalizationCorrections.Should().ContainSingle(correction =>
            correction.Visibility == "clarify"
            && correction.Category == "semantic_spelling_ambiguity");
        response.Data.Trace.PackUsed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_allows_high_confidence_capitol_correction_to_continue()
    {
        var step = new AskNormalizationVerificationRouteStep(
            JourneyTestHelpers.CreateContext(),
            new TraceLookupStore());
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "What is the capitol of Texas?",
            "enterprise",
            null,
            "observe",
            3,
            normalization: LanguageNormalizer.Normalize("What is the capitol of Texas?"));
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.RoutingQuestion.Should().Be("what is the capital of texas");
        context.NormalizationRequiresVerification.Should().BeFalse();
        context.NormalizationConfidence.Should().BeGreaterThanOrEqualTo(AskWorkflowContext.NormalizationContinuationConfidenceThreshold);
    }

    [Fact]
    public async Task ExecuteAsync_blocks_low_confidence_normalization_before_routing()
    {
        var step = new AskNormalizationVerificationRouteStep(
            JourneyTestHelpers.CreateContext(),
            new TraceLookupStore());
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "ambiguous text",
            "enterprise",
            null,
            "observe",
            3,
            normalization: new LanguageNormalizationResult(
                "ambiguous text",
                "ambiguous text",
                Applied: true,
                AppliedRules: new[] { "low_confidence_test" },
                Confidence: 0.70f));
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeFalse();
        response.Data!.Answer.Should().Be("I may have misunderstood the wording. Can you confirm the question you want me to answer?");
        response.Data.Trace.Route.Should().Be("clarification_handler");
        response.Data.Trace.QueryClassification.Should().Be("clarification_required");
        response.Data.Trace.IntentType.Should().Be("needs_clarification");
        response.Data.Trace.NormalizationRequiresVerification.Should().BeTrue();
        response.Data.Trace.NormalizationConfidence.Should().Be(0.70f);
        response.Data.Trace.NormalizationConfidenceThreshold.Should().Be(AskWorkflowContext.NormalizationContinuationConfidenceThreshold);
        response.Data.Trace.PackUsed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_blocks_failed_normalization_before_routing()
    {
        var step = new AskNormalizationVerificationRouteStep(
            JourneyTestHelpers.CreateContext(),
            new TraceLookupStore());
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "???",
            "enterprise",
            null,
            "observe",
            3,
            normalization: new LanguageNormalizationResult(
                "???",
                string.Empty,
                Applied: false,
                AppliedRules: new[] { "normalization_failed" },
                Failed: true,
                FailureReason: "test_failure",
                Confidence: 0.0f));
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeFalse();
        response.Data!.Answer.Should().Be("I did not catch that clearly. Can you rephrase the question?");
        response.Data.Trace.Route.Should().Be("clarification_handler");
        response.Data.Trace.RetrievalSkippedReason.Should().Be("normalization_failed_requires_clarification");
        response.Data.Trace.NormalizationFailed.Should().BeTrue();
        response.Data.Trace.NormalizationConfidence.Should().Be(0.0f);
        response.Data.Trace.PackUsed.Should().BeFalse();
    }
}
