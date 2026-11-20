#nullable enable
namespace Cwi.Orchestrator.Application.Federated;

public sealed class FederatedLearningSummaryOptions
{
    public bool TeacherSubmissionEnabled { get; init; }
    public string TeacherBaseUrl { get; init; } = string.Empty;
    public string TeacherSummaryPath { get; init; } = "/federated-learning/summaries";
    public int MaxObservations { get; init; } = 100;
    public int MaxTagCount { get; init; } = 12;

    public static FederatedLearningSummaryOptions LoadFromEnvironment()
    {
        return new FederatedLearningSummaryOptions
        {
            TeacherSubmissionEnabled = ReadBool("CWI_FEDERATED_SUMMARY_TEACHER_SUBMISSION_ENABLED"),
            TeacherBaseUrl = Environment.GetEnvironmentVariable("CWI_FEDERATED_SUMMARY_TEACHER_BASE_URL")?.Trim() ?? string.Empty,
            TeacherSummaryPath = NormalizePath(Environment.GetEnvironmentVariable("CWI_FEDERATED_SUMMARY_TEACHER_PATH")),
            MaxObservations = ReadInt("CWI_FEDERATED_SUMMARY_MAX_OBSERVATIONS", 100),
            MaxTagCount = ReadInt("CWI_FEDERATED_SUMMARY_MAX_TAG_COUNT", 12)
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
            return "/federated-learning/summaries";
        }

        var trimmed = path.Trim();
        return trimmed.StartsWith("/", StringComparison.Ordinal) ? trimmed : "/" + trimmed;
    }
}
#nullable disable
