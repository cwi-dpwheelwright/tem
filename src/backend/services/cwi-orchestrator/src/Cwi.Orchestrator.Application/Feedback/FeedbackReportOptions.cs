using System.Text.Json;

namespace Cwi.Orchestrator.Application.Feedback;

public sealed class FeedbackReportOptions
{
    public string ScreenshotStorageDirectory { get; init; } = "/srv/cwi/feedback/screenshots";
    public string ScreenshotReferenceBaseUri { get; init; } = "cwi://feedback-screenshots/";
    public int ScreenshotMaxBytes { get; init; } = 5 * 1024 * 1024;
    public string AuditLedgerPath { get; init; } = "/srv/cwi/ledger/feedback_events.jsonl";
    public bool SmartsheetEnabled { get; init; }
    public string SmartsheetBaseUrl { get; init; } = "https://api.smartsheet.com/2.0";
    public string? SmartsheetSheetId { get; init; }
    public string? SmartsheetApiToken { get; init; }
    public IReadOnlyDictionary<string, long> SmartsheetColumnIds { get; init; } = new Dictionary<string, long>();

    public static FeedbackReportOptions LoadFromEnvironment()
    {
        return new FeedbackReportOptions
        {
            ScreenshotStorageDirectory = ReadString("CWI_FEEDBACK_SCREENSHOT_DIR", "/srv/cwi/feedback/screenshots"),
            ScreenshotReferenceBaseUri = ReadFirstString(
                "cwi://feedback-screenshots/",
                "CWI_FEEDBACK_SCREENSHOT_BASE_URL",
                "CWI_FEEDBACK_SCREENSHOT_REFERENCE_BASE_URI"),
            ScreenshotMaxBytes = ReadInt("CWI_FEEDBACK_SCREENSHOT_MAX_BYTES", 5 * 1024 * 1024, 256 * 1024, 20 * 1024 * 1024),
            AuditLedgerPath = ReadString("CWI_FEEDBACK_AUDIT_LEDGER_PATH", "/srv/cwi/ledger/feedback_events.jsonl"),
            SmartsheetEnabled = ReadBool("CWI_FEEDBACK_SMARTSHEET_ENABLED", false),
            SmartsheetBaseUrl = ReadString("CWI_FEEDBACK_SMARTSHEET_BASE_URL", "https://api.smartsheet.com/2.0"),
            SmartsheetSheetId = ReadOptionalString("CWI_FEEDBACK_SMARTSHEET_SHEET_ID"),
            SmartsheetApiToken = ReadOptionalString("SMARTSHEET_ACCESS_TOKEN")
                ?? ReadOptionalString("CWI_FEEDBACK_SMARTSHEET_API_TOKEN"),
            SmartsheetColumnIds = ReadColumnMap("CWI_FEEDBACK_SMARTSHEET_COLUMN_MAP_JSON")
        };
    }

    private static string ReadString(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? ReadOptionalString(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string ReadFirstString(string fallback, params string[] names)
    {
        foreach (var name in names)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return fallback;
    }

    private static bool ReadBool(string name, bool fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim() switch
        {
            "1" => true,
            "0" => false,
            var raw when bool.TryParse(raw, out var parsed) => parsed,
            _ => fallback
        };
    }

    private static int ReadInt(string name, int fallback, int min, int max)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (!int.TryParse(value, out var parsed))
        {
            return fallback;
        }

        return Math.Min(max, Math.Max(min, parsed));
    }

    private static IReadOnlyDictionary<string, long> ReadColumnMap(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, long>>(raw) ?? new Dictionary<string, long>();
            return new Dictionary<string, long>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
