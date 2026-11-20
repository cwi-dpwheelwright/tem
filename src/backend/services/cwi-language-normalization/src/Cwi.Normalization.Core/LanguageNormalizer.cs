namespace Cwi.Normalization.Core;

public static class LanguageNormalizer
{
    private static readonly RuleBasedLanguageNormalizer DefaultNormalizer = new();

    public static LanguageNormalizationResult Normalize(string? prompt)
    {
        return DefaultNormalizer.Normalize(prompt);
    }

    public static string NormalizeForRouting(string? prompt)
    {
        return DefaultNormalizer.NormalizeForRouting(prompt);
    }
}
