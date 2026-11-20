namespace Cwi.Normalization.Core;

public interface ILanguageNormalizer
{
    LanguageNormalizationResult Normalize(string? prompt);

    string NormalizeForRouting(string? prompt);
}
