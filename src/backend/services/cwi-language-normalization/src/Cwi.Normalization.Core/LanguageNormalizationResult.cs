namespace Cwi.Normalization.Core;

public sealed record LanguageCorrection(
    string Original,
    string Normalized,
    string Category,
    string Visibility,
    string Reason);

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
    string EngineVersion = RuleBasedLanguageNormalizer.EngineVersion,
    IReadOnlyList<LanguageCorrection>? Corrections = null,
    string CorrectionDecision = "none",
    string CorrectionRisk = "none",
    string CorrectionVisibility = "none");
