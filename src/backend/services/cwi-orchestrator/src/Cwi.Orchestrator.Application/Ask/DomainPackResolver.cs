namespace Cwi.Orchestrator.Application.Ask;

public static class DomainPackResolver
{
    private static readonly string[] AutoPackNames =
    {
        "auto",
        "default",
        "general",
        "general-pack"
    };

    public static string Resolve(string question, string? requestedPackName, string? requestedPackAnchor)
    {
        if (!string.IsNullOrWhiteSpace(requestedPackAnchor))
        {
            return NormalizeRequestedPack(requestedPackName);
        }

        if (!ShouldAutoResolve(requestedPackName))
        {
            return NormalizeRequestedPack(requestedPackName);
        }

        var normalizedQuestion = LanguageNormalizer.NormalizeForRouting(AskQuestionScope.ExtractPrimaryQuestion(question));
        if (normalizedQuestion.Contains("home node", StringComparison.Ordinal)
            || normalizedQuestion.Contains("home-node", StringComparison.Ordinal))
        {
            return "home-node-pack";
        }

        if (normalizedQuestion.Contains("engineering", StringComparison.Ordinal)
            || normalizedQuestion.Contains("engineer", StringComparison.Ordinal)
            || normalizedQuestion.Contains("developer", StringComparison.Ordinal)
            || normalizedQuestion.Contains("technical", StringComparison.Ordinal))
        {
            return "engineering-pack";
        }

        return "general-pack";
    }

    private static bool ShouldAutoResolve(string? requestedPackName)
    {
        if (string.IsNullOrWhiteSpace(requestedPackName))
        {
            return true;
        }

        return AutoPackNames.Contains(requestedPackName.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeRequestedPack(string? requestedPackName)
    {
        return string.IsNullOrWhiteSpace(requestedPackName)
            ? "general-pack"
            : requestedPackName.Trim();
    }

}
