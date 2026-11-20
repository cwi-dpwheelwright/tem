using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Common.Journey;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.HomeNodes;

public sealed class HomeNodeDeferredAskService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HomeNodeDispatchClient _dispatchClient;
    private readonly IJourneyService _journeyService;

    public HomeNodeDeferredAskService(
        HomeNodeDispatchClient dispatchClient,
        IJourneyService journeyService)
    {
        _dispatchClient = dispatchClient;
        _journeyService = journeyService;
    }

    public async Task<ServiceResponse<AskResponseDto>> GetAsync(
        string tenantId,
        string userId,
        string requestId,
        string workId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tenantId)
            || string.IsNullOrWhiteSpace(userId)
            || string.IsNullOrWhiteSpace(requestId)
            || string.IsNullOrWhiteSpace(workId))
        {
            return ServiceResponse<AskResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id, user_id, request_id, and work_id are required."),
                statusCode: 400,
                message: "Invalid deferred ASK request.");
        }

        var journeyEvents = await _journeyService.ListByRequestIdAsync(requestId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        var linkedEvent = journeyEvents.FirstOrDefault(item =>
            CanView(item, tenantId, userId) &&
            string.Equals(ReadMetadataString(item, "work_id"), workId.Trim(), StringComparison.OrdinalIgnoreCase));

        if (linkedEvent is null)
        {
            return ServiceResponse<AskResponseDto>.Fail(
                new DomainError(ErrorCodes.Forbidden, "Deferred work is not visible to the current user."),
                statusCode: 403,
                message: "Deferred work is not visible to the current user.");
        }

        var status = await _dispatchClient.GetAsync(workId.Trim(), cancellationToken).ConfigureAwait(false);
        if (!status.IsSuccess || status.Data is null)
        {
            return ServiceResponse<AskResponseDto>.Fail(
                status.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "Home-node work status failed."),
                statusCode: status.StatusCode,
                message: status.Message);
        }

        var normalizedStatus = NormalizeStatus(status.Data.Status);
        return ServiceResponse<AskResponseDto>.Success(BuildResponse(status.Data, linkedEvent, normalizedStatus));
    }

    private static AskResponseDto BuildResponse(
        HomeNodeWorkReceipt receipt,
        JourneyEvent linkedEvent,
        string normalizedStatus)
    {
        var executionState = normalizedStatus switch
        {
            "completed" => "completed",
            "failed" => "failed",
            "denied" => "denied",
            _ => "pending"
        };
        var answer = executionState == "completed"
            ? ReadResultString(receipt.Result, "answer") ?? "Local execution completed."
            : string.Empty;
        var trace = ReadResultTrace(receipt.Result) ?? new TracePayload
        {
            RequestId = linkedEvent.RequestId,
            TraceId = linkedEvent.TraceId ?? linkedEvent.RequestId,
            Sources = Array.Empty<string>(),
            Confidence = 0,
            PolicyMode = string.Empty,
            Model = "home-node",
            PackHash = string.Empty,
            ExecutionState = executionState,
            ExecutionTarget = "home_node",
            DeferredWorkId = receipt.WorkId,
            HomeNodeStatus = normalizedStatus
        };

        return new AskResponseDto
        {
            Answer = answer,
            ExecutionState = executionState,
            ExecutionTarget = "home_node",
            DeferredWorkId = receipt.WorkId,
            HomeNodeStatus = normalizedStatus,
            ExecutionMessage = BuildExecutionMessage(executionState, normalizedStatus),
            Citations = Array.Empty<CitationDto>(),
            Trace = trace
        };
    }

    private static string BuildExecutionMessage(string executionState, string status)
    {
        return executionState switch
        {
            "completed" => "Home node completed local execution.",
            "failed" => "Home node reported a local execution failure.",
            "denied" => "Home node denied local execution.",
            _ => $"Home node work is still {status}."
        };
    }

    private static bool CanView(JourneyEvent item, string tenantId, string userId)
    {
        return string.Equals(item.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(item.UserId, userId, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeStatus(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Length == 0 ? "pending" : normalized;
    }

    private static string? ReadMetadataString(JourneyEvent item, string key)
    {
        if (!item.Metadata.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text.Trim(),
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString()?.Trim(),
            JsonElement element => element.ToString(),
            _ => value.ToString()
        };
    }

    private static string? ReadResultString(IReadOnlyDictionary<string, object?> result, string key)
    {
        if (!result.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text when !string.IsNullOrWhiteSpace(text) => text.Trim(),
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString()?.Trim(),
            _ => value.ToString()
        };
    }

    private static TracePayload? ReadResultTrace(IReadOnlyDictionary<string, object?> result)
    {
        if (!result.TryGetValue("trace", out var value) || value is null)
        {
            return null;
        }

        try
        {
            return value is JsonElement element
                ? JsonSerializer.Deserialize<TracePayload>(element.GetRawText(), JsonOptions)
                : JsonSerializer.Deserialize<TracePayload>(JsonSerializer.Serialize(value, JsonOptions), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
