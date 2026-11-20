using Cwi.Normalization.Core;
using FluentAssertions;
using Xunit;

namespace Cwi.Normalization.UnitTests;

public sealed class RuleBasedLanguageNormalizerTests
{
    private readonly RuleBasedLanguageNormalizer _normalizer = new();

    [Fact]
    public void Normalize_rewrites_high_confidence_capitol_public_fact_prompt()
    {
        var result = _normalizer.Normalize("What is the capitol of Texas?");

        result.NormalizedPrompt.Should().Be("what is the capital of texas");
        result.Applied.Should().BeTrue();
        result.AppliedRules.Should().Contain("semantic_spelling");
        result.RequiresVerification.Should().BeFalse();
        result.Confidence.Should().BeGreaterThanOrEqualTo(RuleBasedLanguageNormalizer.ContinuationConfidenceThreshold);
        result.CorrectionDecision.Should().Be("visible_disambiguation");
        result.CorrectionRisk.Should().Be("meaning_shift_possible");
        result.CorrectionVisibility.Should().Be("subtle");
        result.Corrections.Should().ContainSingle(correction =>
            correction.Original == "capitol"
            && correction.Normalized == "capital"
            && correction.Visibility == "subtle");
        result.EngineVersion.Should().Be(RuleBasedLanguageNormalizer.EngineVersion);
    }

    [Fact]
    public void Normalize_marks_bare_capitol_place_prompt_for_verification()
    {
        var result = _normalizer.Normalize("capitol texas");

        result.NormalizedPrompt.Should().Be("capitol texas");
        result.Applied.Should().BeTrue();
        result.AppliedRules.Should().Contain("semantic_spelling_verification");
        result.RequiresVerification.Should().BeTrue();
        result.Confidence.Should().BeLessThan(RuleBasedLanguageNormalizer.ContinuationConfidenceThreshold);
        result.CorrectionDecision.Should().Be("verification_required");
        result.CorrectionRisk.Should().Be("ambiguous_meaning");
        result.CorrectionVisibility.Should().Be("clarify");
        result.VerificationMessage.Should().Be("Did you mean \"capital of Texas\" or \"Texas State Capitol\"?");
        result.VerificationCandidates.Should().BeEquivalentTo("capital of Texas", "Texas State Capitol");
    }

    [Fact]
    public void Normalize_for_routing_keeps_existing_misspelling_behavior()
    {
        var normalized = _normalizer.NormalizeForRouting("whts teh wether tmrw do i need jaket");

        normalized.Should().Be("what is the weather tomorrow do i need jacket");
    }

    [Fact]
    public void Normalize_keeps_messy_but_supported_prompt_above_continuation_threshold()
    {
        var result = _normalizer.Normalize("whts teh wether tmrw do i need jaket");

        result.NormalizedPrompt.Should().Be("what is the weather tomorrow do i need jacket");
        result.RequiresVerification.Should().BeFalse();
        result.Confidence.Should().BeGreaterThanOrEqualTo(RuleBasedLanguageNormalizer.ContinuationConfidenceThreshold);
        result.CorrectionDecision.Should().Be("silent_continue");
        result.CorrectionRisk.Should().Be("low");
        result.CorrectionVisibility.Should().Be("silent");
        result.Corrections.Should().OnlyContain(correction => correction.Visibility == "silent");
    }
}
