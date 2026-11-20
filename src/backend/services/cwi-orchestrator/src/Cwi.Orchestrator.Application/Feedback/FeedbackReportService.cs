using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Feedback;

public sealed class FeedbackReportService
{
    private const int MaxQuestionChars = 16000;
    private const int MaxAnswerChars = 32000;
    private const int MaxCommentChars = 8000;
    private static readonly IReadOnlyDictionary<string, string> SmartsheetFeedbackTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Wrong answer"] = "Wrong answer",
        ["Confusing answer"] = "Confusing answer",
        ["Too long"] = "Too long",
        ["Too short"] = "Too short",
        ["Routing issue"] = "Routing issue",
        ["Memory issue"] = "Memory issue",
        ["Connector issue"] = "Connector issue",
        ["Tone issue"] = "Tone issue",
        ["Other"] = "Other",
        ["Should have asked clarification"] = "Other",
        ["Bad tone"] = "Tone issue",
        ["Missing memory"] = "Memory issue",
        ["Wrong memory used"] = "Memory issue",
        ["Missing connector data"] = "Connector issue",
        ["Wrong connector data"] = "Connector issue",
        ["Missing source/evidence"] = "Other",
        ["Bad confidence score"] = "Other",
        ["UI issue"] = "Other",
        ["Slow response"] = "Other"
    };
    private static readonly HashSet<string> AllowedTypes = new(
        SmartsheetFeedbackTypes.Keys.Append("Helpful"),
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AllowedSeverities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Low",
        "Medium",
        "High",
        "Critical"
    };

    private readonly FeedbackReportOptions _options;
    private readonly ISmartsheetFeedbackClient _smartsheetClient;

    public FeedbackReportService(FeedbackReportOptions options, ISmartsheetFeedbackClient smartsheetClient)
    {
        _options = options;
        _smartsheetClient = smartsheetClient;
    }

    public async Task<ServiceResponse<FeedbackReportResponseDto>> SubmitAsync(
        FeedbackReportRequestDto request,
        string tenantId,
        string userId,
        CancellationToken cancellationToken)
    {
        var validation = Validate(request, tenantId, userId);
        if (validation is not null)
        {
            return ServiceResponse<FeedbackReportResponseDto>.Fail(validation, statusCode: 400, message: "Invalid request");
        }

        var submittedAt = DateTimeOffset.UtcNow;
        var feedbackId = BuildFeedbackId(submittedAt);
        var warnings = new List<string>();
        var screenshot = await TryStoreScreenshotAsync(request, tenantId, feedbackId, submittedAt, warnings, cancellationToken)
            .ConfigureAwait(false);
        var rowValues = BuildSmartsheetValues(request, tenantId, userId, feedbackId, submittedAt, screenshot);
        string? smartsheetRowId = null;
        string? smartsheetSkippedReason = null;

        if (IsHelpfulFeedback(request))
        {
            smartsheetSkippedReason = "smartsheet_helpful_feedback_local_only";
            warnings.Add(smartsheetSkippedReason);
        }
        else
        {
            try
            {
                var row = await _smartsheetClient.CreateRowAsync(rowValues, cancellationToken).ConfigureAwait(false);
                smartsheetRowId = row.RowId;
                smartsheetSkippedReason = row.SkippedReason;
                if (!string.IsNullOrWhiteSpace(row.SkippedReason))
                {
                    warnings.Add(row.SkippedReason);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                smartsheetSkippedReason = "smartsheet_submit_failed";
                warnings.Add(smartsheetSkippedReason);
            }
        }

        await WriteAuditReceiptAsync(
            request,
            tenantId,
            userId,
            feedbackId,
            submittedAt,
            screenshot,
            smartsheetRowId,
            "FEEDBACK_SUBMITTED",
            warnings,
            cancellationToken).ConfigureAwait(false);

        var message = screenshot.Requested && !screenshot.Stored
            ? "Feedback submitted without screenshot."
            : string.IsNullOrWhiteSpace(smartsheetSkippedReason)
                ? "Feedback submitted."
                : "Feedback captured locally.";

        return ServiceResponse<FeedbackReportResponseDto>.Success(new FeedbackReportResponseDto
        {
            Ok = true,
            FeedbackId = feedbackId,
            SmartsheetRowId = smartsheetRowId,
            ScreenshotStored = screenshot.Stored,
            Message = message,
            Warnings = warnings.Count == 0 ? null : warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        });
    }

    private static DomainError? Validate(FeedbackReportRequestDto request, string tenantId, string userId)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(userId))
        {
            return new DomainError(ErrorCodes.InvalidRequest, "tenant_id and user_id are required.");
        }

        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Length > MaxQuestionChars)
        {
            return new DomainError(ErrorCodes.InvalidRequest, "question is required and must fit the feedback limit.");
        }

        if (string.IsNullOrWhiteSpace(request.Answer) || request.Answer.Length > MaxAnswerChars)
        {
            return new DomainError(ErrorCodes.InvalidRequest, "answer is required and must fit the feedback limit.");
        }

        if (!AllowedTypes.Contains(request.Feedback.Type))
        {
            return new DomainError(ErrorCodes.InvalidRequest, "feedback.type is invalid.");
        }

        if (!AllowedSeverities.Contains(request.Feedback.Severity))
        {
            return new DomainError(ErrorCodes.InvalidRequest, "feedback.severity is invalid.");
        }

        if (request.Feedback.Comment?.Length > MaxCommentChars)
        {
            return new DomainError(ErrorCodes.InvalidRequest, "feedback.comment must fit the feedback limit.");
        }

        return null;
    }

    private async Task<StoredScreenshot> TryStoreScreenshotAsync(
        FeedbackReportRequestDto request,
        string tenantId,
        string feedbackId,
        DateTimeOffset submittedAt,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (request.Screenshot?.Included != true)
        {
            return StoredScreenshot.NotRequested();
        }

        try
        {
            if (!TryDecodePngDataUrl(request.Screenshot.DataUrl, out var bytes)
                || bytes.Length == 0
                || bytes.Length > _options.ScreenshotMaxBytes
                || !HasPngSignature(bytes))
            {
                warnings.Add("screenshot_invalid_or_too_large");
                return StoredScreenshot.Failed(requested: true);
            }

            var fileName = BuildScreenshotFileName(tenantId, request.RequestId, submittedAt);
            var tenantScope = Slug(tenantId, "tenant");
            var directory = Path.Combine(_options.ScreenshotStorageDirectory, tenantScope);
            Directory.CreateDirectory(directory);
            var storageKey = $"{tenantScope}/{fileName}";
            var path = Path.Combine(directory, fileName);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new StoredScreenshot(
                Requested: true,
                Stored: true,
                StorageKey: storageKey,
                StorageUrl: BuildScreenshotReference(storageKey),
                Sha256: hash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            warnings.Add("screenshot_storage_failed");
            return StoredScreenshot.Failed(requested: true);
        }
    }

    private IReadOnlyDictionary<string, object?> BuildSmartsheetValues(
        FeedbackReportRequestDto request,
        string tenantId,
        string userId,
        string feedbackId,
        DateTimeOffset submittedAt,
        StoredScreenshot screenshot)
    {
        return new Dictionary<string, object?>
        {
            ["Submitted At"] = submittedAt.UtcDateTime.ToString("O"),
            ["Tenant ID"] = tenantId,
            ["Tenant Name"] = Truncate(request.TenantName, 500),
            ["User ID"] = userId,
            ["User Email"] = Truncate(request.UserEmail, 500),
            ["User Role"] = Truncate(request.UserRole, 500),
            ["Conversation ID"] = Truncate(request.ConversationId, 500),
            ["Request ID"] = Truncate(request.RequestId, 500),
            ["Trace ID"] = Truncate(request.TraceId, 500),
            ["Selected Pack"] = Truncate(request.SelectedPack?.PackId, 500),
            ["Selected Pack Version"] = Truncate(request.SelectedPack?.PackVersion, 500),
            ["Selected Domain"] = Truncate(request.SelectedPack?.Domain, 500),
            ["Confidence"] = request.Confidence,
            ["Route"] = Truncate(request.RuntimeTrace.Route, 500),
            ["Intent"] = Truncate(request.RuntimeTrace.Intent, 500),
            ["Feedback Type"] = NormalizeFeedbackTypeForSmartsheet(request.Feedback.Type),
            ["Severity"] = request.Feedback.Severity,
            ["User Comment"] = Truncate(request.Feedback.Comment, 4000),
            ["Question"] = Truncate(request.Question, 4000),
            ["Answer"] = Truncate(request.Answer, 4000),
            ["Screenshot Included"] = screenshot.Requested,
            ["Screenshot Stored"] = screenshot.Stored,
            ["Screenshot Storage Key"] = screenshot.StorageKey,
            ["Screenshot Link"] = screenshot.StorageUrl,
            ["Screenshot SHA256"] = screenshot.Sha256,
            ["Status"] = "New",
            ["Priority"] = ResolvePriority(request),
            ["Memory Used"] = request.RuntimeTrace.MemoryUsed,
            ["Graph Used"] = request.RuntimeTrace.GraphUsed,
            ["Retrieval Used"] = request.RuntimeTrace.RetrievalUsed,
            ["Pack Used"] = request.RuntimeTrace.PackUsed,
            ["Connector Context Used"] = request.RuntimeTrace.ConnectorContextUsed,
            ["Latency MS"] = request.RuntimeTrace.LatencyMs,
            ["Frontend Version"] = Truncate(request.Client.FrontendAppVersion, 500),
            ["Backend Version"] = Truncate(request.Client.BackendRuntimeVersion, 500),
            ["Browser / User Agent"] = Truncate(request.Client.BrowserUserAgent, 1000),
            ["Feedback Receipt ID"] = feedbackId
        };
    }

    private async Task WriteAuditReceiptAsync(
        FeedbackReportRequestDto request,
        string tenantId,
        string userId,
        string feedbackId,
        DateTimeOffset submittedAt,
        StoredScreenshot screenshot,
        string? smartsheetRowId,
        string eventName,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        var audit = new
        {
            @event = eventName,
            event_name = eventName,
            event_type = eventName,
            feedback_id = feedbackId,
            submitted_at = submittedAt.UtcDateTime,
            tenant_id = tenantId,
            user_id = userId,
            request_id = request.RequestId,
            trace_id = request.TraceId,
            feedback_type = request.Feedback.Type,
            severity = request.Feedback.Severity,
            screenshot_included = screenshot.Requested,
            screenshot_stored = screenshot.Stored,
            screenshot_storage_key = screenshot.StorageKey,
            screenshot_sha256 = screenshot.Sha256,
            smartsheet_row_id = smartsheetRowId,
            warnings = warnings.Count == 0 ? null : warnings
        };

        Directory.CreateDirectory(Path.GetDirectoryName(_options.AuditLedgerPath) ?? ".");
        var line = JsonSerializer.Serialize(audit) + Environment.NewLine;
        await File.AppendAllTextAsync(_options.AuditLedgerPath, line, cancellationToken).ConfigureAwait(false);
    }

    private string BuildScreenshotReference(string storageKey)
    {
        var baseUri = string.IsNullOrWhiteSpace(_options.ScreenshotReferenceBaseUri)
            ? "cwi://feedback-screenshots/"
            : _options.ScreenshotReferenceBaseUri.Trim();
        return baseUri.EndsWith("/", StringComparison.Ordinal) ? $"{baseUri}{storageKey}" : $"{baseUri}/{storageKey}";
    }

    private static string ResolvePriority(FeedbackReportRequestDto request)
    {
        if (IsHelpfulFeedback(request))
        {
            return "Low";
        }

        return request.Feedback.Severity;
    }

    private static string NormalizeFeedbackTypeForSmartsheet(string feedbackType)
    {
        return SmartsheetFeedbackTypes.TryGetValue(feedbackType, out var normalized)
            ? normalized
            : "Other";
    }

    private static bool IsHelpfulFeedback(FeedbackReportRequestDto request)
    {
        return string.Equals(request.Feedback.Type, "Helpful", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryDecodePngDataUrl(string? dataUrl, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        const string prefix = "data:image/png;base64,";
        if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bytes = Convert.FromBase64String(dataUrl[prefix.Length..]);
        return true;
    }

    private static bool HasPngSignature(byte[] bytes)
    {
        ReadOnlySpan<byte> signature = stackalloc byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
        return bytes.AsSpan().StartsWith(signature);
    }

    private static string BuildFeedbackId(DateTimeOffset submittedAt)
    {
        return $"fbk_{submittedAt:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}"[..33];
    }

    private static string BuildScreenshotFileName(string tenantId, string? requestId, DateTimeOffset submittedAt)
    {
        var tenant = Slug(tenantId, "tenant");
        var request = Slug(string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N")[..8] : requestId, "request");
        return $"feedback_{tenant}_{request}_{submittedAt:yyyyMMddTHHmmssZ}.png";
    }

    private static string Slug(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = new StringBuilder();
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '-');
        }

        var slug = builder.ToString().Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? fallback : slug[..Math.Min(80, slug.Length)];
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private sealed record StoredScreenshot(
        bool Requested,
        bool Stored,
        string? StorageKey = null,
        string? StorageUrl = null,
        string? Sha256 = null)
    {
        public static StoredScreenshot NotRequested() => new(false, false);

        public static StoredScreenshot Failed(bool requested) => new(requested, false);
    }
}
