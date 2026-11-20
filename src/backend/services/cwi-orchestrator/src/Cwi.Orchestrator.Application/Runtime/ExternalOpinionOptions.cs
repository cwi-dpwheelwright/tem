#nullable enable
namespace Cwi.Orchestrator.Application.Runtime;

public sealed class ExternalOpinionOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = string.Empty;
    public string EvaluationPath { get; init; } = "/evaluate";
    public int MaxPromptSummaryChars { get; init; } = 600;
    public int MaxContextSummaryChars { get; init; } = 1200;

    public static ExternalOpinionOptions LoadFromEnvironment()
    {
        return new ExternalOpinionOptions
        {
            Enabled = ReadBool("CWI_EXTERNAL_OPINION_ENABLED"),
            BaseUrl = Environment.GetEnvironmentVariable("CWI_EXTERNAL_OPINION_BASE_URL")?.Trim() ?? string.Empty,
            EvaluationPath = NormalizePath(Environment.GetEnvironmentVariable("CWI_EXTERNAL_OPINION_PATH")),
            MaxPromptSummaryChars = ReadInt("CWI_EXTERNAL_OPINION_MAX_PROMPT_SUMMARY_CHARS", 600),
            MaxContextSummaryChars = ReadInt("CWI_EXTERNAL_OPINION_MAX_CONTEXT_SUMMARY_CHARS", 1200)
        };
    }

    private static bool ReadBool(string key)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return !string.IsNullOrWhiteSpace(value)
               && value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    private static int ReadInt(string key, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : defaultValue;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/evaluate";
        }

        var trimmed = path.Trim();
        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed;
    }
}
#nullable disable
