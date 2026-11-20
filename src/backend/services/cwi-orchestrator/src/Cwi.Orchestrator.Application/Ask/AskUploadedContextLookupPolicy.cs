#nullable enable
using System.Text;
using Cwi.Orchestrator.Application.Workflows;

namespace Cwi.Orchestrator.Application.Ask;

public sealed record UploadedContextLookupSnapshot(
    string? QueryClassification,
    string? QueryClassificationReason,
    string? IntentType,
    bool RetrievalAllowed,
    string GroundingMode,
    string? RetrievalSkippedReason,
    string? RetrievalDomain,
    IReadOnlyList<string> RetrievalDomainTerms,
    float RetrievalMinScore,
    bool EvidenceRequired);

public static class AskUploadedContextLookupPolicy
{
    public const string Classification = "uploaded_context_lookup";
    private const float MinScore = 0.35f;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about", "according", "after", "again", "also", "answer", "are", "based", "before",
        "built", "can", "could", "created", "describe", "define", "developed", "does",
        "explain", "from", "general", "give", "have", "into", "know", "made", "make",
        "mean", "means", "please", "platform", "source", "sources", "tell", "that",
        "the", "this", "understand", "what", "when", "where",
        "which", "with", "would", "you", "your"
    };

    public static bool TryApply(AskWorkflowContext context, out UploadedContextLookupSnapshot snapshot)
    {
        snapshot = Capture(context);
        if (!ShouldAttempt(context))
        {
            return false;
        }

        var terms = ExtractLookupTerms(context.RoutingQuestion);
        if (terms.Count == 0)
        {
            return false;
        }

        context.QueryClassification = Classification;
        context.QueryClassificationReason = "Plain entity or platform question may match user-uploaded context; trying governed user memory before native general knowledge.";
        context.IntentType = "context_lookup";
        context.RetrievalAllowed = true;
        context.GroundingMode = "source_grounded";
        context.RetrievalSkippedReason = null;
        context.RetrievalDomain = "uploaded_user_context";
        context.RetrievalDomainTerms = terms;
        context.RetrievalMinScore = Math.Max(MinScore, context.RetrievalMinScore);
        context.EvidenceRequired = false;
        return true;
    }

    public static void Restore(AskWorkflowContext context, UploadedContextLookupSnapshot snapshot)
    {
        context.QueryClassification = snapshot.QueryClassification;
        context.QueryClassificationReason = snapshot.QueryClassificationReason;
        context.IntentType = snapshot.IntentType;
        context.RetrievalAllowed = snapshot.RetrievalAllowed;
        context.GroundingMode = snapshot.GroundingMode;
        context.RetrievalSkippedReason = snapshot.RetrievalSkippedReason;
        context.RetrievalDomain = snapshot.RetrievalDomain;
        context.RetrievalDomainTerms = snapshot.RetrievalDomainTerms;
        context.RetrievalMinScore = snapshot.RetrievalMinScore;
        context.EvidenceRequired = snapshot.EvidenceRequired;
    }

    public static bool IsUploadedContextLookup(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, Classification, StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.IntentType, "context_lookup", StringComparison.OrdinalIgnoreCase)
               || string.Equals(context.RetrievalDomain, "uploaded_user_context", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<string> ExtractLookupTerms(string? question)
    {
        var normalized = LanguageNormalizer.NormalizeForRouting(
            AskQuestionScope.ExtractPrimaryQuestion(question));
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Array.Empty<string>();
        }

        return Tokenize(normalized)
            .Where(token => !StopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private static bool ShouldAttempt(AskWorkflowContext context)
    {
        if (context.SkipModelInference || context.RetrievalAllowed || context.EvidenceRequired)
        {
            return false;
        }

        if (LooksLikeSelfContextDisclosure(context.RoutingQuestion))
        {
            return false;
        }

        if (LooksLikeSelfProfileQuestion(context.RoutingQuestion))
        {
            return false;
        }

        if (LooksLikeNativePublicFactQuestion(context.RoutingQuestion))
        {
            return false;
        }

        if (!string.Equals(context.QueryClassification, "general_model_knowledge", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(context.IntentType, "explanation", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsNativeAnswerContext(context) && ExtractLookupTerms(context.RoutingQuestion).Count > 0;
    }

    private static bool LooksLikeSelfContextDisclosure(string? question)
    {
        var primary = AskQuestionScope.ExtractPrimaryQuestion(question);
        if (string.IsNullOrWhiteSpace(primary)
            || primary.Contains('?', StringComparison.Ordinal))
        {
            return false;
        }

        var normalized = LanguageNormalizer.NormalizeForRouting(primary);
        return System.Text.RegularExpressions.Regex.IsMatch(
                   normalized,
                   @"\b(?:my name is|i am named|im named|i'm named|call me)\s+[a-z]",
                   System.Text.RegularExpressions.RegexOptions.CultureInvariant)
               || System.Text.RegularExpressions.Regex.IsMatch(
                   normalized,
                   @"\b(?:i work as|i am|im|i'm|my job is|my profession is|my role is)\s+(?:a|an|the)?\s*(?:[a-z'’-]+\s+){0,4}(?:engineer|developer|architect|designer|analyst|manager|consultant|administrator|technician|programmer|operator|director|specialist)\b",
               System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static bool LooksLikeSelfProfileQuestion(string? question)
    {
        var normalized = LanguageNormalizer.NormalizeForRouting(
            AskQuestionScope.ExtractPrimaryQuestion(question));
        return UserMemoryRecallPromptPolicy.IsMatch(normalized);
    }

    private static bool LooksLikeNativePublicFactQuestion(string? question)
    {
        var primary = AskQuestionScope.ExtractPrimaryQuestion(question);
        if (string.IsNullOrWhiteSpace(primary))
        {
            return false;
        }

        var normalized = LanguageNormalizer.NormalizeForRouting(primary);
        return System.Text.RegularExpressions.Regex.IsMatch(
                   normalized,
                   @"\b(?:what(?: is|'s|s)?|which city is|name)\b.{0,40}\bcapital(?: city)? of\b",
                   System.Text.RegularExpressions.RegexOptions.CultureInvariant)
               || System.Text.RegularExpressions.Regex.IsMatch(
                   normalized,
                   @"\bcapital(?: city)? of\s+[a-z][a-z\s]{1,60}\??$",
                   System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static UploadedContextLookupSnapshot Capture(AskWorkflowContext context)
    {
        return new UploadedContextLookupSnapshot(
            context.QueryClassification,
            context.QueryClassificationReason,
            context.IntentType,
            context.RetrievalAllowed,
            context.GroundingMode,
            context.RetrievalSkippedReason,
            context.RetrievalDomain,
            context.RetrievalDomainTerms,
            context.RetrievalMinScore,
            context.EvidenceRequired);
    }

    private static bool IsNativeAnswerContext(AskWorkflowContext context)
    {
        return string.Equals(context.QueryClassification, "general_model_knowledge", StringComparison.OrdinalIgnoreCase)
               && string.Equals(context.IntentType, "explanation", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                continue;
            }

            foreach (var token in FlushToken(builder))
            {
                yield return token;
            }
        }

        foreach (var token in FlushToken(builder))
        {
            yield return token;
        }
    }

    private static IEnumerable<string> FlushToken(StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            yield break;
        }

        var token = builder.ToString();
        builder.Clear();
        if (token.Length >= 3)
        {
            yield return token;
        }
    }
}
#nullable disable
