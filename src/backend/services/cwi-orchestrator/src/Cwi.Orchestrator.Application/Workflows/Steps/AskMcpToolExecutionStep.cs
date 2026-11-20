#nullable enable
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Mcp;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class AskMcpToolExecutionStep : IAskWorkflowStep
{
    private const double ToolIntentConfidence = 0.86d;

    private readonly McpExecutionOptions _options;
    private readonly IMcpRegistryClient _registryClient;
    private readonly IMcpPolicyClient _policyClient;
    private readonly McpPolicyEvaluator _policyEvaluator;
    private readonly IMcpToolExecutor _executor;
    private readonly McpActionLedgerRecorder _ledgerRecorder;

    public AskMcpToolExecutionStep(
        McpExecutionOptions options,
        IMcpRegistryClient registryClient,
        IMcpPolicyClient policyClient,
        McpPolicyEvaluator policyEvaluator,
        IMcpToolExecutor executor,
        McpActionLedgerRecorder ledgerRecorder)
    {
        _options = options;
        _registryClient = registryClient;
        _policyClient = policyClient;
        _policyEvaluator = policyEvaluator;
        _executor = executor;
        _ledgerRecorder = ledgerRecorder;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (!IsToolIntent(context.IntentType))
        {
            return await next().ConfigureAwait(false);
        }

        if (_options.IsDisabled)
        {
            return await next().ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(context.TraceId))
        {
            context.TraceId = Guid.NewGuid().ToString("N");
        }

        var requestedToolName = ResolveExecutionToolName(context);
        var registryResponse = await _registryClient.GetRegistryAsync(cancellationToken).ConfigureAwait(false);
        if (!registryResponse.IsSuccess || registryResponse.Data is null)
        {
            return ServiceResponse<AskResponseDto>.Fail(
                registryResponse.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "MCP tool registry unavailable."),
                statusCode: registryResponse.StatusCode,
                message: registryResponse.Message);
        }

        var policyResponse = await _policyClient.GetPolicyAsync(cancellationToken).ConfigureAwait(false);
        if (!policyResponse.IsSuccess || policyResponse.Data is null)
        {
            return ServiceResponse<AskResponseDto>.Fail(
                policyResponse.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "MCP policy unavailable."),
                statusCode: policyResponse.StatusCode,
                message: policyResponse.Message);
        }

        var tool = registryResponse.Data.Tools.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, requestedToolName, StringComparison.OrdinalIgnoreCase));
        var policy = _policyEvaluator.Evaluate(
            requestedToolName,
            tool,
            policyResponse.Data,
            _options,
            ToolIntentConfidence);

        context.McpTool = tool;
        context.McpPolicy = policy;
        context.McpExecutionToolName = tool?.Name ?? requestedToolName;
        context.McpPolicyDecision = policy.Blocked
            ? "blocked"
            : policy.RequiresApproval
                ? "approval_required"
                : "allowed";

        if (policy.Blocked)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = "blocked",
                ["tool"] = requestedToolName,
                ["reason"] = policy.Reason
            };

            await RecordAsync(context, requestedToolName, tool, policy, executed: false, result, cancellationToken).ConfigureAwait(false);
            ApplyToolAnswer(context, BuildBlockedAnswer(requestedToolName, policy.Reason), result, executed: false);
            return await next().ConfigureAwait(false);
        }

        if (policy.RequiresApproval)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = "approval_required",
                ["tool"] = requestedToolName,
                ["reason"] = policy.Reason
            };

            await RecordAsync(context, requestedToolName, tool, policy, executed: false, result, cancellationToken).ConfigureAwait(false);
            ApplyToolAnswer(context, BuildApprovalAnswer(requestedToolName, policy.Reason), result, executed: false);
            return await next().ConfigureAwait(false);
        }

        if (tool is null)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["status"] = "blocked",
                ["tool"] = requestedToolName,
                ["reason"] = "Tool is not registered in the MCP registry."
            };

            await RecordAsync(context, requestedToolName, tool, policy, executed: false, result, cancellationToken).ConfigureAwait(false);
            ApplyToolAnswer(context, BuildBlockedAnswer(requestedToolName, "Tool is not registered in the MCP registry."), result, executed: false);
            return await next().ConfigureAwait(false);
        }

        var payload = BuildPayload(context, requestedToolName);
        var execution = await _executor
            .ExecuteAsync(tool, context.TenantId, context.UserId, context.TraceId, payload, cancellationToken)
            .ConfigureAwait(false);

        var executionResult = execution.IsSuccess && execution.Data is not null
            ? execution.Data
            : new McpExecutionResult(false, new Dictionary<string, object?>
            {
                ["status"] = "failed",
                ["tool"] = requestedToolName,
                ["failure_reason"] = execution.Message ?? execution.Error?.Message ?? "MCP tool execution failed."
            });

        await RecordAsync(
            context,
            requestedToolName,
            tool,
            policy,
            executionResult.Executed,
            executionResult.Result,
            cancellationToken).ConfigureAwait(false);

        ApplyToolAnswer(
            context,
            BuildExecutionAnswer(context.IntentType, requestedToolName, executionResult.Result, context.TraceId),
            executionResult.Result,
            executionResult.Executed);

        return await next().ConfigureAwait(false);
    }

    private static string ResolveExecutionToolName(AskWorkflowContext context)
    {
        if (string.Equals(context.IntentType, "news.get", StringComparison.OrdinalIgnoreCase)
            || string.Equals(context.IntentType, "sports.get", StringComparison.OrdinalIgnoreCase))
        {
            return "search.web";
        }

        return context.IntentType ?? string.Empty;
    }

    private static Dictionary<string, object?> BuildPayload(AskWorkflowContext context, string requestedToolName)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["raw_input"] = context.RawQuestion,
            ["normalized_input"] = context.RoutingQuestion,
            ["query"] = BuildQuery(context, requestedToolName),
            ["intent"] = context.IntentType,
            ["execution_tool"] = requestedToolName,
            ["intent_confidence"] = ToolIntentConfidence,
            ["response_mode"] = context.TemplateUsed,
            ["current_utc"] = nowUtc,
            ["current_date"] = nowUtc.ToString("yyyy-MM-dd"),
            ["timezone"] = context.EffectiveTimezone ?? "UTC",
            ["tenant_id"] = context.TenantId,
            ["user_id"] = context.UserId,
            ["conversation_id"] = context.ConversationId,
            ["request_id"] = context.RequestId,
            ["trace_id"] = context.TraceId
        };

        var forecastWindow = ResolveForecastWindow(context.RoutingQuestion);
        if (!string.IsNullOrWhiteSpace(forecastWindow))
        {
            payload["forecast_window"] = forecastWindow;
            payload["date"] = forecastWindow;
        }

        var detailLevel = ResolveDetailLevel(context.RoutingQuestion);
        if (!string.IsNullOrWhiteSpace(detailLevel))
        {
            payload["detail_level"] = detailLevel;
        }

        if (string.Equals(requestedToolName, "ledger.view", StringComparison.OrdinalIgnoreCase))
        {
            payload["scope"] = ResolveLedgerScope(context.RoutingQuestion);
        }

        var location = ExtractLocation(context.RoutingQuestion);
        if (!string.IsNullOrWhiteSpace(location))
        {
            payload["location"] = location;
        }

        if (!string.IsNullOrWhiteSpace(context.PackName))
        {
            payload["pack"] = context.PackName;
            payload["pack_name"] = context.PackName;
        }

        return payload;
    }

    private static string BuildQuery(AskWorkflowContext context, string requestedToolName)
    {
        if (string.Equals(context.IntentType, "news.get", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeSearchQuery($"latest news {DateTimeOffset.UtcNow:yyyy} {context.RoutingQuestion}");
        }

        if (string.Equals(context.IntentType, "sports.get", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeSearchQuery($"sports current standings score {DateTimeOffset.UtcNow:yyyy} {context.RoutingQuestion}");
        }

        if (string.Equals(requestedToolName, "search.web", StringComparison.OrdinalIgnoreCase)
            && ToolOrCurrentInfoPromptPolicy.HasFreshnessCue(context.RoutingQuestion))
        {
            return NormalizeSearchQuery($"current {DateTimeOffset.UtcNow:yyyy-MM-dd} {context.RoutingQuestion}");
        }

        return NormalizeSearchQuery(context.RoutingQuestion);
    }

    private static string NormalizeSearchQuery(string query)
    {
        var normalized = LanguageNormalizer.NormalizeForRouting(query);
        normalized = Regex.Replace(
            normalized,
            @"\b(?:look up|use web and answer|use the web|find out|tell me|show me|please|what is|what are|whats|what's)\b",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"[^\p{L}\p{N}\s-]", " ", RegexOptions.CultureInvariant);
        normalized = Regex.Replace(normalized, @"\s+", " ", RegexOptions.CultureInvariant).Trim();

        var year = DateTimeOffset.UtcNow.Year.ToString(CultureInfo.InvariantCulture);
        var populationMatch = Regex.Match(
            normalized,
            @"\bpopulation\s+(?:of\s+)?(?<place>[a-z][a-z\s]+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (populationMatch.Success)
        {
            return $"{populationMatch.Groups["place"].Value.Trim()} population {year} census";
        }

        if (normalized.Contains("latest", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("news", StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalized} {year}";
        }

        return normalized;
    }

    private static string? ResolveForecastWindow(string question)
    {
        var normalized = LanguageNormalizer.NormalizeForRouting(question);
        if (normalized.Contains("tomorrow", StringComparison.Ordinal)
            || normalized.Contains("tmrw", StringComparison.Ordinal))
        {
            return "tomorrow";
        }

        if (normalized.Contains("today", StringComparison.Ordinal))
        {
            return "today";
        }

        return null;
    }

    private static string? ResolveDetailLevel(string question)
    {
        var normalized = LanguageNormalizer.NormalizeForRouting(question);
        if (normalized.Contains("more detail", StringComparison.Ordinal)
            || normalized.Contains("details", StringComparison.Ordinal)
            || normalized.Contains("hourly", StringComparison.Ordinal)
            || normalized.Contains("forecast", StringComparison.Ordinal))
        {
            return "detailed";
        }

        if (normalized.Contains("jacket", StringComparison.Ordinal)
            || normalized.Contains("wear", StringComparison.Ordinal))
        {
            return "clothing";
        }

        return null;
    }

    private static string ResolveLedgerScope(string question)
    {
        var normalized = LanguageNormalizer.NormalizeForRouting(question);
        if (normalized.Contains("current trace", StringComparison.Ordinal)
            || normalized.Contains("this trace", StringComparison.Ordinal))
        {
            return "current_trace";
        }

        if (normalized.Contains("today", StringComparison.Ordinal))
        {
            return "today";
        }

        return "last_24_hours";
    }

    private static string? ExtractLocation(string question)
    {
        var patterns = new[]
        {
            @"\b(?:in|near)\s+(?<location>[a-z][a-z\s\.-]{2,}?)(?=,|\?|$|\s+(?:do|should|will|can|with)\b)",
            @"\bfor\s+(?!(?:today|tomorrow|tmrw)\b)(?<location>[a-z][a-z\s\.-]{2,}?)(?=,|\?|$|\s+(?:do|should|will|can|with)\b)"
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(question, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (match.Success)
            {
                return match.Groups["location"].Value.Trim(' ', '.', '?', ',');
            }
        }

        return null;
    }

    private async Task RecordAsync(
        AskWorkflowContext context,
        string actionType,
        McpToolDefinition? tool,
        McpPolicyEvaluation policy,
        bool executed,
        IReadOnlyDictionary<string, object?> result,
        CancellationToken cancellationToken)
    {
        var status = TryGetString(result, "status") ?? (executed ? "executed" : "not_executed");
        await _ledgerRecorder.RecordAsync(
            context.TraceId!,
            context.TenantId,
            context.UserId,
            actionType,
            tool,
            policy,
            executed,
            status,
            result,
            cancellationToken).ConfigureAwait(false);
    }

    private static void ApplyToolAnswer(
        AskWorkflowContext context,
        string answer,
        Dictionary<string, object?> result,
        bool executed)
    {
        context.Answer = answer;
        context.McpResult = result;
        context.McpExecuted = executed;
        context.McpExecutionStatus = TryGetString(result, "status") ?? (executed ? "executed" : "not_executed");
        context.Route = executed ? "mcp_tool_execution" : "mcp_tool_status";
        context.GroundingMode = "mcp_tool";
        context.Grounded = executed;
        context.ReasoningBased = false;
        context.RetrievalAllowed = false;
        context.RetrievalSkippedReason = executed ? "mcp_tool_executed" : "mcp_tool_not_executed";
        context.ModelName = "mcp-read-only-executor";
        context.SkipModelInference = true;
        context.ExecutionTarget = "mcp_tool";
        context.ExecutionState = ResolveExecutionState(context.McpExecutionStatus, executed);
        context.Retrieved.Clear();
        context.GraphEvidence.Clear();
        context.MemoryResults = Array.Empty<GovernedMemoryResult>();
        context.SourceResults = Array.Empty<GovernedSourceResult>();
    }

    private static string ResolveExecutionState(string? status, bool executed)
    {
        if (executed)
        {
            return "completed";
        }

        return status?.ToLowerInvariant() switch
        {
            "blocked" => "blocked_by_policy",
            "approval_required" => "approval_required",
            "failed" or "provider_failed" or "provider_unavailable" => "failed",
            _ => "blocked_by_policy"
        };
    }

    private static string BuildBlockedAnswer(string toolName, string reason)
    {
        return $"I understood this as `{toolName}`, but I cannot execute it in the current MCP policy mode. Reason: {reason}";
    }

    private static string BuildApprovalAnswer(string toolName, string reason)
    {
        return $"I understood this as `{toolName}`. That action requires approval before execution. Reason: {reason}";
    }

    private static string BuildExecutionAnswer(
        string? originalIntent,
        string toolName,
        IReadOnlyDictionary<string, object?> result,
        string traceId)
    {
        var status = TryGetString(result, "status") ?? "not_executed";
        if (!string.Equals(status, "executed", StringComparison.OrdinalIgnoreCase))
        {
            var message = TryGetString(result, "message")
                ?? TryGetString(result, "failure_reason")
                ?? "The tool did not return an executable result.";
            return $"{ToolLabel(toolName)} did not complete: {message}";
        }

        return toolName.ToLowerInvariant() switch
        {
            "weather.get" => BuildWeatherAnswer(result, traceId),
            "search.web" => BuildSearchAnswer(originalIntent, result, traceId),
            "system.status" => BuildSystemStatusAnswer(result, traceId),
            "node.health.check" => BuildNodeHealthAnswer(result, traceId),
            "ledger.view" => BuildLedgerAnswer(result, traceId),
            "pack.list" => BuildPackListAnswer(result, traceId),
            "pack.get" => BuildSimpleToolAnswer("The requested pack was inspected.", toolName, result, traceId),
            "pin.resolve" => BuildPinAnswer(result, traceId),
            _ => BuildSimpleToolAnswer("The read-only MCP tool completed.", toolName, result, traceId)
        };
    }

    private static string BuildWeatherAnswer(IReadOnlyDictionary<string, object?> result, string traceId)
    {
        var response = TryGetJson(result, "response");
        var forecastWindow = TryGetString(result, "forecast_window") ?? TryGetString(result, "date");
        var detailLevel = TryGetString(result, "detail_level");
        var current = response is null ? null : FirstArrayObject(response.Value, "current_condition");
        var tempF = current is null ? null : TryReadString(current.Value, "temp_F");
        var desc = current is null ? null : FirstNestedString(current.Value, "weatherDesc", "value");

        if (string.Equals(forecastWindow, "tomorrow", StringComparison.OrdinalIgnoreCase)
            && response is not null
            && TryGetForecastDay(response.Value, dayIndex: 1, out var tomorrow))
        {
            var date = TryReadString(tomorrow, "date") ?? "tomorrow";
            var high = TryReadString(tomorrow, "maxtempF");
            var low = TryReadString(tomorrow, "mintempF");
            var average = TryReadString(tomorrow, "avgtempF");
            var hourly = FirstArrayObject(tomorrow, "hourly");
            var tomorrowDesc = CleanWeatherDescription(hourly is null ? null : FirstNestedString(hourly.Value, "weatherDesc", "value"));
            var summary = $"Tomorrow ({date}) is forecast around {average ?? "unknown"} F";
            if (!string.IsNullOrWhiteSpace(low) || !string.IsNullOrWhiteSpace(high))
            {
                summary += $" with a low/high of {low ?? "?"}/{high ?? "?"} F";
            }

            if (!string.IsNullOrWhiteSpace(tomorrowDesc))
            {
                summary += $" and {tomorrowDesc}";
            }

            summary += ".";
            var wantsClothingAdvice = string.Equals(detailLevel, "clothing", StringComparison.OrdinalIgnoreCase);
            var jacketLooksUseful = (int.TryParse(average, out var avgTemp) && avgTemp <= 60)
                                    || (int.TryParse(low, out var lowTemp) && lowTemp <= 60);
            var jacket = jacketLooksUseful
                ? " A jacket is a good idea."
                : wantsClothingAdvice
                    ? " You probably do not need a jacket, unless you will be out during the cooler part of the day."
                    : string.Empty;
            return $"{summary}{jacket}\n\nSource: wttr.in";
        }

        var currentSummary = !string.IsNullOrWhiteSpace(tempF)
            ? $"The weather provider reports about {tempF} F{(string.IsNullOrWhiteSpace(desc) ? string.Empty : $" and {CleanWeatherDescription(desc)}")}."
            : "The weather provider returned forecast data.";

        if (string.Equals(detailLevel, "detailed", StringComparison.OrdinalIgnoreCase)
            && response is not null
            && TryGetForecastDay(response.Value, dayIndex: 0, out var today))
        {
            var high = TryReadString(today, "maxtempF");
            var low = TryReadString(today, "mintempF");
            if (!string.IsNullOrWhiteSpace(low) || !string.IsNullOrWhiteSpace(high))
            {
                currentSummary += $" Today's low/high is {low ?? "?"}/{high ?? "?"} F.";
            }
        }

        var currentWantsClothingAdvice = string.Equals(detailLevel, "clothing", StringComparison.OrdinalIgnoreCase);
        var currentJacketLooksUseful = int.TryParse(tempF, out var parsedTemp) && parsedTemp <= 60;
        var currentJacket = currentJacketLooksUseful
            ? " You will likely want a jacket."
            : currentWantsClothingAdvice
                ? " You probably do not need a jacket based on the current temperature."
                : string.Empty;

        return $"{currentSummary}{currentJacket}\n\nSource: wttr.in";
    }

    private static string BuildSearchAnswer(string? originalIntent, IReadOnlyDictionary<string, object?> result, string traceId)
    {
        var results = TryGetDictionaryArray(result, "results");
        var prefix = string.Equals(originalIntent, "news.get", StringComparison.OrdinalIgnoreCase)
            ? "I checked current web search results for this news request."
            : string.Equals(originalIntent, "sports.get", StringComparison.OrdinalIgnoreCase)
                ? "I checked current web search results for this sports request."
                : "I checked the read-only web search provider.";

        if (results.Length == 0)
        {
            return $"{prefix} The provider returned no displayable title/snippet results for this query.\n\nSource: DuckDuckGo";
        }

        var lines = results
            .Take(3)
            .Select((item, index) =>
            {
                var title = DictionaryString(item, "title") ?? "Search result";
                var snippet = DictionaryString(item, "snippet");
                var url = DictionaryString(item, "url");
                var source = DictionaryString(item, "source") ?? "DuckDuckGo";
                var sentence = $"{index + 1}. {title}";
                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    sentence += $": {snippet}";
                }

                if (!string.IsNullOrWhiteSpace(url))
                {
                    sentence += $" ({url})";
                }

                sentence += $" [{source}]";
                return sentence;
            });

        return $"{prefix}\n{string.Join('\n', lines)}\n\nSource: DuckDuckGo";
    }

    private static string? CleanWeatherDescription(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().TrimEnd('.');
    }

    private static string BuildLedgerAnswer(IReadOnlyDictionary<string, object?> result, string traceId)
    {
        var events = TryGetDictionaryArray(result, "events");
        var scope = TryGetString(result, "scope") ?? "last_24_hours";
        if (events.Length == 0)
        {
            return $"Ledger lookup completed for {scope}. No permitted MCP action receipt events were found.\n\nSource: CWI action ledger";
        }

        var lines = events
            .Take(8)
            .Select(item =>
            {
                var created = DictionaryString(item, "created_utc") ?? "unknown time";
                var action = DictionaryString(item, "action_type") ?? DictionaryString(item, "event_type") ?? "action";
                var status = DictionaryString(item, "status") ?? "unknown";
                var executed = DictionaryString(item, "executed");
                return $"- {created}: {action} status {status} executed {executed ?? "unknown"}";
            });
        return $"Ledger lookup completed for {scope}. Recent MCP action receipts:\n{string.Join('\n', lines)}\n\nSource: CWI action ledger";
    }

    private static string BuildSystemStatusAnswer(IReadOnlyDictionary<string, object?> result, string traceId)
    {
        var components = TryGetDictionaryArray(result, "components");
        if (components.Length == 0)
        {
            return BuildSimpleToolAnswer("CWI system status completed.", "system.status", result, traceId);
        }

        var lines = components.Select(item =>
        {
            var name = DictionaryString(item, "name") ?? "component";
            var status = DictionaryString(item, "status") ?? "unknown";
            var detail = DictionaryString(item, "detail");
            return $"- {name}: {status}{(string.IsNullOrWhiteSpace(detail) ? string.Empty : $" - {detail}")}";
        });
        return $"CWI health snapshot:\n{string.Join('\n', lines)}\n\nSource: CWI system health";
    }

    private static string BuildNodeHealthAnswer(IReadOnlyDictionary<string, object?> result, string traceId)
    {
        var nodes = TryGetDictionaryArray(result, "nodes");
        if (nodes.Length == 0)
        {
            return $"No registered nodes were found for this tenant.\n\nSource: CWI node registry";
        }

        var lines = nodes.Select(item =>
        {
            var nodeId = DictionaryString(item, "node_id") ?? "node";
            var status = DictionaryString(item, "status") ?? "UNKNOWN";
            var lastHeartbeat = DictionaryString(item, "last_heartbeat_utc");
            var ip = DictionaryString(item, "tailscale_ip");
            return $"- {nodeId}: {status}{(string.IsNullOrWhiteSpace(ip) ? string.Empty : $" ({ip})")}{(string.IsNullOrWhiteSpace(lastHeartbeat) ? string.Empty : $", last heartbeat {lastHeartbeat}")}";
        });
        return $"Node health snapshot:\n{string.Join('\n', lines)}\n\nSource: CWI node registry";
    }

    private static string BuildPackListAnswer(IReadOnlyDictionary<string, object?> result, string traceId)
    {
        var packs = TryGetStringArray(result, "packs");
        var summary = packs.Length == 0
            ? "No mounted packs were returned."
            : $"Mounted packs include: {string.Join(", ", packs.Take(12))}.";
        return $"{summary}\n\nSource: CWI pack registry";
    }

    private static string BuildPinAnswer(IReadOnlyDictionary<string, object?> result, string traceId)
    {
        var mounted = TryGetObject(result, "mounted") as bool?;
        var summary = mounted == true
            ? "The pins mount is available to the orchestrator."
            : "The pins mount is not available to the orchestrator.";
        return $"{summary}\n\nSource: CWI pin resolver";
    }

    private static string BuildSimpleToolAnswer(
        string message,
        string toolName,
        IReadOnlyDictionary<string, object?> result,
        string traceId)
    {
        var toolMessage = TryGetString(result, "message");
        return $"{message}{(string.IsNullOrWhiteSpace(toolMessage) ? string.Empty : $" {toolMessage}")}\n\nSource: {ToolLabel(toolName)}";
    }

    private static string ToolLabel(string toolName)
    {
        return toolName switch
        {
            "weather.get" => "Weather lookup",
            "search.web" => "Web search",
            "system.status" => "System status check",
            "node.health.check" => "Node health check",
            "ledger.view" => "Ledger lookup",
            _ => "MCP tool"
        };
    }

    private static bool IsToolIntent(string? intentType)
    {
        return string.Equals(intentType, "weather.get", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "news.get", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "sports.get", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "search.web", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "system.status", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "node.health.check", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "ledger.view", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "pack.list", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "pack.get", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "pin.resolve", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "calendar.view", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "email.read", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "email.send", StringComparison.OrdinalIgnoreCase)
               || string.Equals(intentType, "workflow.start", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetString(IReadOnlyDictionary<string, object?> result, string key)
    {
        if (!result.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => value.ToString()
        };
    }

    private static object? TryGetObject(IReadOnlyDictionary<string, object?> result, string key)
    {
        return result.TryGetValue(key, out var value) ? value : null;
    }

    private static JsonElement? TryGetJson(IReadOnlyDictionary<string, object?> result, string key)
    {
        return result.TryGetValue(key, out var value) && value is JsonElement element ? element : null;
    }

    private static Dictionary<string, object?>[] TryGetDictionaryArray(
        IReadOnlyDictionary<string, object?> result,
        string key)
    {
        if (!result.TryGetValue(key, out var value) || value is null)
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        return value switch
        {
            Dictionary<string, object?>[] dictionaries => dictionaries,
            IEnumerable<Dictionary<string, object?>> dictionaries => dictionaries.ToArray(),
            object[] objects => objects
                .Select(ToDictionary)
                .Where(item => item is not null)
                .Select(item => item!)
                .ToArray(),
            JsonElement element when element.ValueKind == JsonValueKind.Array => element
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object)
                .Select(item => item.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => JsonValueToObject(property.Value),
                    StringComparer.OrdinalIgnoreCase))
                .ToArray(),
            _ => Array.Empty<Dictionary<string, object?>>()
        };
    }

    private static Dictionary<string, object?>? ToDictionary(object? value)
    {
        if (value is Dictionary<string, object?> dictionary)
        {
            return dictionary;
        }

        if (value is IReadOnlyDictionary<string, object?> readOnly)
        {
            return new Dictionary<string, object?>(readOnly, StringComparer.OrdinalIgnoreCase);
        }

        return null;
    }

    private static object? JsonValueToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => element.ToString()
        };
    }

    private static string? DictionaryString(IReadOnlyDictionary<string, object?> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            JsonElement element => element.ToString(),
            _ => value.ToString()
        };
    }

    private static JsonElement? FirstArrayObject(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                return item;
            }
        }

        return null;
    }

    private static bool TryGetForecastDay(JsonElement response, int dayIndex, out JsonElement day)
    {
        day = default;
        if (!response.TryGetProperty("weather", out var weather)
            || weather.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var index = 0;
        foreach (var item in weather.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (index == dayIndex)
            {
                day = item;
                return true;
            }

            index++;
        }

        return false;
    }

    private static string? FirstNestedString(JsonElement element, string arrayPropertyName, string nestedPropertyName)
    {
        var item = FirstArrayObject(element, arrayPropertyName);
        return item is null ? null : TryReadString(item.Value, nestedPropertyName);
    }

    private static string? TryReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.ToString(),
            _ => null
        };
    }

    private static string[] TryGetStringArray(IReadOnlyDictionary<string, object?> result, string key)
    {
        if (!result.TryGetValue(key, out var value) || value is null)
        {
            return Array.Empty<string>();
        }

        return value switch
        {
            string[] strings => strings,
            IEnumerable<string> strings => strings.ToArray(),
            JsonElement element when element.ValueKind == JsonValueKind.Array => element
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)
                .ToArray(),
            _ => Array.Empty<string>()
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }
}
#nullable disable
