#nullable enable
using CoreLanguageNormalizationResult = Cwi.Normalization.Core.LanguageNormalizationResult;
using CoreLanguageNormalizer = Cwi.Normalization.Core.LanguageNormalizer;

namespace Cwi.Orchestrator.Application.Ask;

public sealed record LanguageNormalizationResult(
    string RawPrompt,
    string NormalizedPrompt,
    bool Applied,
    IReadOnlyList<string> AppliedRules,
    bool Failed = false,
    string? FailureReason = null,
    bool RequiresVerification = false,
    string? VerificationMessage = null,
    IReadOnlyList<string>? VerificationCandidates = null,
    float Confidence = 1.0f,
    string? EngineVersion = null,
    IReadOnlyList<LanguageCorrection>? Corrections = null,
    string CorrectionDecision = "none",
    string CorrectionRisk = "none",
    string CorrectionVisibility = "none");

public sealed record LanguageCorrection(
    string Original,
    string Normalized,
    string Category,
    string Visibility,
    string Reason);

public static class LanguageNormalizer
{
    public static LanguageNormalizationResult Normalize(string? prompt)
    {
        return Map(CoreLanguageNormalizer.Normalize(prompt));
    }

    public static string NormalizeForRouting(string? prompt)
    {
        return CoreLanguageNormalizer.NormalizeForRouting(prompt);
    }

    public static LanguageNormalizationResult FromCore(CoreLanguageNormalizationResult result)
    {
        return Map(result);
    }

    private static LanguageNormalizationResult Map(CoreLanguageNormalizationResult result)
    {
        return new LanguageNormalizationResult(
            result.RawPrompt,
            result.NormalizedPrompt,
            result.Applied,
            result.AppliedRules,
            result.Failed,
            result.FailureReason,
            result.RequiresVerification,
            result.VerificationMessage,
            result.VerificationCandidates,
            result.Confidence,
            result.EngineVersion,
            result.Corrections?.Select(correction => new LanguageCorrection(
                    correction.Original,
                    correction.Normalized,
                    correction.Category,
                    correction.Visibility,
                    correction.Reason))
                .ToArray(),
            result.CorrectionDecision,
            result.CorrectionRisk,
            result.CorrectionVisibility);
    }
}
#nullable disable
