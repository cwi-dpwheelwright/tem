using System.Text.Json.Serialization;

namespace Cwi.Orchestrator.Application.Models;

public sealed class FeedbackReportRequestDto
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; init; } = string.Empty;

    [JsonPropertyName("tenant_name")]
    public string? TenantName { get; init; }

    [JsonPropertyName("user_id")]
    public string UserId { get; init; } = string.Empty;

    [JsonPropertyName("user_email")]
    public string? UserEmail { get; init; }

    [JsonPropertyName("user_role")]
    public string? UserRole { get; init; }

    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    [JsonPropertyName("conversation_id")]
    public string? ConversationId { get; init; }

    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    [JsonPropertyName("trace_id")]
    public string? TraceId { get; init; }

    [JsonPropertyName("selected_pack")]
    public FeedbackSelectedPackDto? SelectedPack { get; init; }

    [JsonPropertyName("question")]
    public string Question { get; init; } = string.Empty;

    [JsonPropertyName("answer")]
    public string Answer { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double? Confidence { get; init; }

    [JsonPropertyName("runtime_trace")]
    public FeedbackRuntimeTraceDto RuntimeTrace { get; init; } = new();

    [JsonPropertyName("feedback")]
    public FeedbackUserInputDto Feedback { get; init; } = new();

    [JsonPropertyName("screenshot")]
    public FeedbackScreenshotDto? Screenshot { get; init; }

    [JsonPropertyName("client")]
    public FeedbackClientContextDto Client { get; init; } = new();
}

public sealed class FeedbackSelectedPackDto
{
    [JsonPropertyName("pack_id")]
    public string? PackId { get; init; }

    [JsonPropertyName("pack_version")]
    public string? PackVersion { get; init; }

    [JsonPropertyName("domain")]
    public string? Domain { get; init; }
}

public sealed class FeedbackRuntimeTraceDto
{
    [JsonPropertyName("route")]
    public string? Route { get; init; }

    [JsonPropertyName("intent")]
    public string? Intent { get; init; }

    [JsonPropertyName("model_used")]
    public string? ModelUsed { get; init; }

    [JsonPropertyName("memory_used")]
    public bool? MemoryUsed { get; init; }

    [JsonPropertyName("graph_used")]
    public bool? GraphUsed { get; init; }

    [JsonPropertyName("retrieval_used")]
    public bool? RetrievalUsed { get; init; }

    [JsonPropertyName("pack_used")]
    public bool? PackUsed { get; init; }

    [JsonPropertyName("connector_context_used")]
    public bool? ConnectorContextUsed { get; init; }

    [JsonPropertyName("latency_ms")]
    public long? LatencyMs { get; init; }
}

public sealed class FeedbackUserInputDto
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = "Medium";

    [JsonPropertyName("comment")]
    public string? Comment { get; init; }
}

public sealed class FeedbackScreenshotDto
{
    [JsonPropertyName("included")]
    public bool Included { get; init; }

    [JsonPropertyName("data_url")]
    public string? DataUrl { get; init; }
}

public sealed class FeedbackClientContextDto
{
    [JsonPropertyName("ui_page")]
    public string? UiPage { get; init; }

    [JsonPropertyName("frontend_app_version")]
    public string? FrontendAppVersion { get; init; }

    [JsonPropertyName("backend_runtime_version")]
    public string? BackendRuntimeVersion { get; init; }

    [JsonPropertyName("browser_user_agent")]
    public string? BrowserUserAgent { get; init; }
}

public sealed class FeedbackReportResponseDto
{
    [JsonPropertyName("ok")]
    public bool Ok { get; init; }

    [JsonPropertyName("feedback_id")]
    public string FeedbackId { get; init; } = string.Empty;

    [JsonPropertyName("smartsheet_row_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SmartsheetRowId { get; init; }

    [JsonPropertyName("screenshot_stored")]
    public bool ScreenshotStored { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("warnings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Warnings { get; init; }
}
