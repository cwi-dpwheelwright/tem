#nullable enable
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Cwi.Normalization.Core;

public sealed class RuleBasedLanguageNormalizer : ILanguageNormalizer
{
    public const string EngineVersion = "cwi-rule-normalizer-v1";
    public const float ContinuationConfidenceThreshold = 0.85f;

    private static readonly Regex WhitespacePattern = new(@"\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex EdgePunctuationPattern = new("^[\\?\\!\\,\\:\\;\\\"\\u201c\\u201d\\(\\)\\[\\]\\{\\}]+|[\\?\\!\\,\\:\\;\\\"\\u201c\\u201d\\(\\)\\[\\]\\{\\}]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CapitolPublicFactPattern = new(
        @"^\s*what\s+(?:is|are)\s+(?:the\s+)?capitol\s+(?<preposition>of|in|for)\s+(?<place>[a-z][a-z\s\.'-]{1,80})\s*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex BareCapitolPlacePattern = new(
        @"^\s*(?:what\s+(?:is|are)\s+)?(?:the\s+)?capitol\s+(?<place>[a-z][a-z\s\.'-]{1,80})\s*\??\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string> TokenReplacements =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["wht"] = "what",
            ["wat"] = "what",
            ["whts"] = "what is",
            ["wats"] = "what is",
            ["wut"] = "what",
            ["whats"] = "what is",
            ["whos"] = "who is",
            ["hows"] = "how is",
            ["wheres"] = "where is",
            ["whre"] = "where",
            ["wer"] = "where",
            ["wher"] = "where",
            ["wen"] = "when",
            ["whn"] = "when",
            ["hw"] = "how",
            ["hav"] = "have",
            ["tha"] = "the",
            ["teh"] = "the",
            ["todays"] = "today's",
            ["tday"] = "today",
            ["dat"] = "date",
            ["tmrw"] = "tomorrow",
            ["tomorow"] = "tomorrow",
            ["wether"] = "weather",
            ["jaket"] = "jacket",
            ["newz"] = "news",
            ["nite"] = "night",
            ["sumarize"] = "summarize",
            ["pyhton"] = "python",
            ["rsk"] = "risk",
            ["hlp"] = "help",
            ["reed"] = "read",
            ["pls"] = "please",
            ["plz"] = "please",
            ["thx"] = "thanks"
        };

    private static readonly IReadOnlyDictionary<string, string> PhraseReplacements =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["what's"] = "what is",
            ["wht's"] = "what is",
            ["wat's"] = "what is",
            ["who's"] = "who is",
            ["where's"] = "where is",
            ["when's"] = "when is",
            ["how's"] = "how is",
            ["it's"] = "it is",
            ["that's"] = "that is",
            ["there's"] = "there is",
            ["i'm"] = "i am",
            ["i've"] = "i have",
            ["i'd"] = "i would",
            ["can't"] = "cannot",
            ["cant"] = "cannot",
            ["won't"] = "will not",
            ["wont"] = "will not",
            ["don't"] = "do not",
            ["dont"] = "do not",
            ["doesn't"] = "does not",
            ["doesnt"] = "does not",
            ["isn't"] = "is not",
            ["isnt"] = "is not",
            ["aren't"] = "are not",
            ["arent"] = "are not",
            ["couldn't"] = "could not",
            ["couldnt"] = "could not",
            ["shouldn't"] = "should not",
            ["shouldnt"] = "should not",
            ["wouldn't"] = "would not",
            ["wouldnt"] = "would not"
        };

    public LanguageNormalizationResult Normalize(string? prompt)
    {
        var raw = prompt ?? string.Empty;
        try
        {
            var normalized = NormalizeForRouting(raw, out var rules, out var corrections);
            var verification = BuildVerificationMetadata(normalized);
            var appliedRules = rules.Concat(verification.AppliedRules).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var allCorrections = corrections.ToList();
            if (verification.RequiresVerification && verification.Candidates.Count > 0)
            {
                allCorrections.Add(new LanguageCorrection(
                    Original: ExtractAmbiguousTerm(normalized),
                    Normalized: string.Join(" | ", verification.Candidates),
                    Category: "semantic_spelling_ambiguity",
                    Visibility: "clarify",
                    Reason: "correction_changes_possible_meaning"));
            }

            var correctionDecision = ResolveCorrectionDecision(verification.RequiresVerification, allCorrections);
            var correctionVisibility = ResolveCorrectionVisibility(correctionDecision);
            var correctionRisk = ResolveCorrectionRisk(verification.RequiresVerification, allCorrections);
            return new LanguageNormalizationResult(
                raw,
                verification.NormalizedPrompt,
                !string.Equals(CollapseWhitespace(raw).ToLowerInvariant(), verification.NormalizedPrompt, StringComparison.Ordinal)
                || verification.AppliedRules.Count > 0,
                appliedRules,
                RequiresVerification: verification.RequiresVerification,
                VerificationMessage: verification.Message,
                VerificationCandidates: verification.Candidates,
                Confidence: CalculateConfidence(raw, verification.NormalizedPrompt, appliedRules, verification.RequiresVerification),
                Corrections: allCorrections.Count == 0 ? null : allCorrections,
                CorrectionDecision: correctionDecision,
                CorrectionRisk: correctionRisk,
                CorrectionVisibility: correctionVisibility);
        }
        catch (Exception ex)
        {
            return new LanguageNormalizationResult(
                raw,
                string.Empty,
                false,
                new[] { "normalization_failed" },
                Failed: true,
                FailureReason: ex.GetType().Name,
                Confidence: 0.0f);
        }
    }

    public string NormalizeForRouting(string? prompt)
    {
        try
        {
            return NormalizeForRouting(prompt, out _, out _);
        }
        catch
        {
            return SafeFallbackForRouting(prompt);
        }
    }

    private static string NormalizeForRouting(
        string? prompt,
        out IReadOnlyList<string> appliedRules,
        out IReadOnlyList<LanguageCorrection> corrections)
    {
        var rules = new List<string>();
        var correctionRecords = new List<LanguageCorrection>();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            appliedRules = rules;
            corrections = correctionRecords;
            return string.Empty;
        }

        var normalized = prompt
            .Trim()
            .ToLowerInvariant()
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'')
            .Replace('\u201c', '"')
            .Replace('\u201d', '"');

        var beforePhrases = normalized;
        foreach (var replacement in PhraseReplacements)
        {
            normalized = Regex.Replace(
                normalized,
                $@"\b{Regex.Escape(replacement.Key)}\b",
                replacement.Value,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        if (!string.Equals(beforePhrases, normalized, StringComparison.Ordinal))
        {
            rules.Add("contractions");
        }

        var rebuilt = new StringBuilder();
        var tokenRuleApplied = false;
        foreach (var rawToken in WhitespacePattern.Split(normalized))
        {
            var token = CleanToken(rawToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            if (TokenReplacements.TryGetValue(token, out var replacement))
            {
                AppendToken(rebuilt, replacement);
                correctionRecords.Add(new LanguageCorrection(
                    Original: token,
                    Normalized: replacement,
                    Category: "misspelling",
                    Visibility: "silent",
                    Reason: "high_confidence_common_typo"));
                tokenRuleApplied = true;
                continue;
            }

            AppendToken(rebuilt, token);
        }

        if (tokenRuleApplied)
        {
            rules.Add("misspellings");
        }

        normalized = CollapseWhitespace(rebuilt.ToString());
        normalized = ApplyHighConfidenceSemanticCorrections(normalized, rules, correctionRecords);
        if (!string.Equals(CollapseWhitespace(prompt), normalized, StringComparison.OrdinalIgnoreCase))
        {
            rules.Add("punctuation_case_whitespace");
        }

        appliedRules = rules.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        corrections = correctionRecords;
        return normalized;
    }

    private static string ApplyHighConfidenceSemanticCorrections(
        string normalized,
        List<string> rules,
        List<LanguageCorrection> corrections)
    {
        var match = CapitolPublicFactPattern.Match(normalized);
        if (!match.Success)
        {
            return normalized;
        }

        rules.Add("semantic_spelling");
        corrections.Add(new LanguageCorrection(
            Original: "capitol",
            Normalized: "capital",
            Category: "semantic_spelling",
            Visibility: "subtle",
            Reason: "spelling_changes_possible_meaning"));
        return Regex.Replace(
            normalized,
            @"\bcapitol\b",
            "capital",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(50));
    }

    private static NormalizationVerification BuildVerificationMetadata(string normalized)
    {
        var match = BareCapitolPlacePattern.Match(normalized);
        if (!match.Success || CapitolPublicFactPattern.IsMatch(normalized))
        {
            return NormalizationVerification.None(normalized);
        }

        var place = FormatPlace(match.Groups["place"].Value);
        if (string.IsNullOrWhiteSpace(place))
        {
            return NormalizationVerification.None(normalized);
        }

        var candidates = new[]
        {
            $"capital of {place}",
            $"{place} State Capitol"
        };
        return new NormalizationVerification(
            normalized,
            RequiresVerification: true,
            Message: $"Did you mean \"{candidates[0]}\" or \"{candidates[1]}\"?",
            Candidates: candidates,
            AppliedRules: new[] { "semantic_spelling_verification" });
    }

    private static float CalculateConfidence(
        string raw,
        string normalized,
        IReadOnlyCollection<string> rules,
        bool requiresVerification)
    {
        if (requiresVerification)
        {
            return 0.58f;
        }

        if (string.IsNullOrWhiteSpace(raw) && string.IsNullOrWhiteSpace(normalized))
        {
            return 1.0f;
        }

        var confidence = 1.0f;
        if (rules.Contains("semantic_spelling", StringComparer.OrdinalIgnoreCase))
        {
            confidence = Math.Min(confidence, 0.96f);
        }

        if (rules.Contains("misspellings", StringComparer.OrdinalIgnoreCase))
        {
            confidence = Math.Min(confidence, 0.91f);
        }

        if (rules.Contains("contractions", StringComparer.OrdinalIgnoreCase))
        {
            confidence = Math.Min(confidence, 0.97f);
        }

        if (rules.Count > 2)
        {
            confidence = Math.Min(confidence, 0.88f);
        }

        return Math.Max(0.0f, Math.Min(1.0f, confidence));
    }

    private static string ResolveCorrectionDecision(
        bool requiresVerification,
        IReadOnlyList<LanguageCorrection> corrections)
    {
        if (requiresVerification)
        {
            return "verification_required";
        }

        if (corrections.Any(correction =>
                string.Equals(correction.Visibility, "subtle", StringComparison.OrdinalIgnoreCase)))
        {
            return "visible_disambiguation";
        }

        return corrections.Count > 0 ? "silent_continue" : "none";
    }

    private static string ResolveCorrectionVisibility(string correctionDecision)
    {
        return correctionDecision switch
        {
            "verification_required" => "clarify",
            "visible_disambiguation" => "subtle",
            "silent_continue" => "silent",
            _ => "none"
        };
    }

    private static string ResolveCorrectionRisk(
        bool requiresVerification,
        IReadOnlyList<LanguageCorrection> corrections)
    {
        if (requiresVerification)
        {
            return "ambiguous_meaning";
        }

        if (corrections.Any(correction =>
                string.Equals(correction.Reason, "spelling_changes_possible_meaning", StringComparison.OrdinalIgnoreCase)))
        {
            return "meaning_shift_possible";
        }

        return corrections.Count > 0 ? "low" : "none";
    }

    private static string ExtractAmbiguousTerm(string normalized)
    {
        var firstToken = WhitespacePattern.Split(normalized)
            .FirstOrDefault(token => !string.IsNullOrWhiteSpace(token));
        return string.IsNullOrWhiteSpace(firstToken) ? normalized : firstToken;
    }

    private static string FormatPlace(string value)
    {
        var cleaned = CollapseWhitespace(value)
            .Trim()
            .TrimEnd('?', '.', '!', ',', ';', ':')
            .Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return string.Empty;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(cleaned.ToLowerInvariant());
    }

    private static string CleanToken(string token)
    {
        return EdgePunctuationPattern.Replace(token, string.Empty).Trim();
    }

    private static void AppendToken(StringBuilder builder, string token)
    {
        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(token);
    }

    private static string CollapseWhitespace(string value)
    {
        return WhitespacePattern.Replace(value.Trim(), " ");
    }

    private static string SafeFallbackForRouting(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private sealed record NormalizationVerification(
        string NormalizedPrompt,
        bool RequiresVerification,
        string? Message,
        IReadOnlyList<string> Candidates,
        IReadOnlyList<string> AppliedRules)
    {
        public static NormalizationVerification None(string normalizedPrompt)
        {
            return new NormalizationVerification(
                normalizedPrompt,
                RequiresVerification: false,
                Message: null,
                Candidates: Array.Empty<string>(),
                AppliedRules: Array.Empty<string>());
        }
    }
}
#nullable disable
