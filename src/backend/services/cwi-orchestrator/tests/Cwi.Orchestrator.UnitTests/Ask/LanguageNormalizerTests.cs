using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Workflows;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Ask;

public sealed class LanguageNormalizerTests
{
    [Fact]
    public void Normalize_rewrites_capitol_public_fact_prompt_without_blocking_verification()
    {
        var result = LanguageNormalizer.Normalize("What is the capitol of Texas?");

        result.NormalizedPrompt.Should().Be("what is the capital of texas");
        result.Applied.Should().BeTrue();
        result.AppliedRules.Should().Contain("semantic_spelling");
        result.RequiresVerification.Should().BeFalse();
        result.Confidence.Should().BeGreaterThanOrEqualTo(AskWorkflowContext.NormalizationContinuationConfidenceThreshold);
        result.VerificationMessage.Should().BeNull();
        result.CorrectionDecision.Should().Be("visible_disambiguation");
        result.CorrectionRisk.Should().Be("meaning_shift_possible");
        result.CorrectionVisibility.Should().Be("subtle");
        result.Corrections.Should().ContainSingle(correction =>
            correction.Original == "capitol"
            && correction.Normalized == "capital"
            && correction.Visibility == "subtle");
    }

    [Fact]
    public void Normalize_requires_verification_for_bare_capitol_place_prompt()
    {
        var result = LanguageNormalizer.Normalize("capitol texas");

        result.NormalizedPrompt.Should().Be("capitol texas");
        result.AppliedRules.Should().Contain("semantic_spelling_verification");
        result.RequiresVerification.Should().BeTrue();
        result.Confidence.Should().BeLessThan(AskWorkflowContext.NormalizationContinuationConfidenceThreshold);
        result.VerificationMessage.Should().Be("Did you mean \"capital of Texas\" or \"Texas State Capitol\"?");
        result.VerificationCandidates.Should().BeEquivalentTo("capital of Texas", "Texas State Capitol");
        result.CorrectionDecision.Should().Be("verification_required");
        result.CorrectionRisk.Should().Be("ambiguous_meaning");
        result.CorrectionVisibility.Should().Be("clarify");
    }
}
