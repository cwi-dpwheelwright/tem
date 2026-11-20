#nullable enable
using System.Text.Json;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Mcp;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Policy;
using Cwi.Orchestrator.Application.Survival;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Workflows.Steps;

public sealed class ActionDecisionStep : IActionWorkflowStep
{
    private readonly IPolicyEvaluator _policyEvaluator;
    private readonly IApprovalTicketRepository _approvalTicketRepository;

    public ActionDecisionStep(
        IPolicyEvaluator policyEvaluator,
        IApprovalTicketRepository approvalTicketRepository)
    {
        _policyEvaluator = policyEvaluator;
        _approvalTicketRepository = approvalTicketRepository;
    }

    public Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        return ExecuteInternalAsync(context, next, cancellationToken);
    }

    private async Task<ServiceResponse<ActionResponseDto>> ExecuteInternalAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (context.McpPolicyBlocked)
        {
            context.Executed = false;
            context.Result = new Dictionary<string, object?>
            {
                ["status"] = "blocked",
                ["reason"] = context.McpPolicy?.Reason ?? context.DecisionReason ?? "Blocked by MCP policy.",
                ["action_type"] = context.ActionType,
                ["policy_mode"] = context.PolicyMode
            };

            return await next().ConfigureAwait(false);
        }

        if (context.McpRequiresApproval)
        {
            context.Risk = string.IsNullOrWhiteSpace(context.Risk) ? "medium" : context.Risk;
            context.DecisionReason = context.McpPolicy?.Reason ?? "Approval required by MCP policy.";
            return await CreateApprovalTicketAsync(context, next, cancellationToken).ConfigureAwait(false);
        }

        if (context.SimulationDecision is not null)
        {
            context.Risk = context.SimulationFeasibility?.Severity ?? "high";
            context.DecisionReason = context.SimulationDecision.Reason;

            if (context.SimulationDecision.RequiresApproval)
            {
                return await CreateApprovalTicketAsync(context, next, cancellationToken).ConfigureAwait(false);
            }

            if (!context.SimulationDecision.AllowExecution)
            {
                context.Executed = false;
                context.Result = new Dictionary<string, object?>
                {
                    ["status"] = "blocked",
                    ["reason"] = context.DecisionReason,
                    ["risk"] = context.Risk
                };

                return await next().ConfigureAwait(false);
            }
        }

        if (context.GuardrailDecision is not null)
        {
            context.Risk = context.GuardrailSeverity ?? "medium";
            context.DecisionReason = context.GuardrailDecision.Reason;

            if (context.GuardrailDecision.RequiresApproval)
            {
                return await CreateApprovalTicketAsync(context, next, cancellationToken).ConfigureAwait(false);
            }

            if (!context.GuardrailDecision.AllowExecution)
            {
                context.Executed = false;
                context.Result = new Dictionary<string, object?>
                {
                    ["status"] = "blocked",
                    ["reason"] = context.DecisionReason,
                    ["risk"] = context.Risk
                };

                return await next().ConfigureAwait(false);
            }
        }

        var decisionResponse = _policyEvaluator.Evaluate(context.PolicyMode, context.ActionType, context.Payload);
        if (!decisionResponse.IsSuccess || decisionResponse.Data is null)
        {
            return ServiceResponse<ActionResponseDto>.Fail(
                decisionResponse.Error ?? new DomainError(ErrorCodes.InvalidRequest, "Invalid policy."),
                statusCode: decisionResponse.StatusCode,
                message: decisionResponse.Message);
        }

        var decision = decisionResponse.Data;
        context.Risk = decision.Risk.ToString().ToLowerInvariant();
        context.DecisionReason = decision.Reason;

        if (decision.Risk == RiskLevel.High && IsHighDiscrepancy(context.Payload))
        {
            context.DecisionReason = "High discrepancy requires approval for risky action.";
            return await CreateApprovalTicketAsync(context, next, cancellationToken).ConfigureAwait(false);
        }

        if (decision.RequiresApproval)
        {
            return await CreateApprovalTicketAsync(context, next, cancellationToken).ConfigureAwait(false);
        }

        if (decision.AllowExecution)
        {
            context.Executed = true;
            context.Result = new Dictionary<string, object?>
            {
                ["status"] = "executed",
                ["action_type"] = context.ActionType,
                ["risk"] = context.Risk,
                ["policy_mode"] = context.PolicyMode
            };
        }

        return await next().ConfigureAwait(false);
    }

    private static bool IsHighDiscrepancy(IReadOnlyDictionary<string, object?> payload)
    {
        if (TryGetString(payload, "discrepancy_severity", out var severity) ||
            TryGetString(payload, "discrepancySeverity", out severity))
        {
            if (string.Equals(severity, "high", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (TryGetDouble(payload, "discrepancy_score", out var score) ||
            TryGetDouble(payload, "discrepancyScore", out score))
        {
            if (score >= 0.75)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetString(
        IReadOnlyDictionary<string, object?> payload,
        string key,
        out string? value)
    {
        value = null;
        if (!payload.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        if (raw is string text)
        {
            value = text;
            return true;
        }

        if (raw is JsonElement element && element.ValueKind == JsonValueKind.String)
        {
            value = element.GetString();
            return true;
        }

        return false;
    }
    private static bool TryGetDouble(
        IReadOnlyDictionary<string, object?> payload,
        string key,
        out double value)
    {
        value = 0;
        if (!payload.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case double doubleValue:
                value = doubleValue;
                return true;
            case float floatValue:
                value = floatValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            case decimal decimalValue:
                value = (double)decimalValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number:
                return element.TryGetDouble(out value);
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                return double.TryParse(element.GetString(), out value);
            case string text:
                return double.TryParse(text, out value);
            default:
                return false;
        }
    }

    private async Task<ServiceResponse<ActionResponseDto>> CreateApprovalTicketAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = $"{context.TenantId}:{context.UserId}:{context.ActionType}:{context.PolicyMode}";
        var existing = await _approvalTicketRepository
            .GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            context.Executed = false;
            context.Risk = existing.Risk;
            context.DecisionReason = existing.Reason;
            context.ApprovalTicket = new ApprovalTicketDto
            {
                TicketId = existing.TicketId,
                Risk = existing.Risk,
                Reason = existing.Reason
            };

            return await next().ConfigureAwait(false);
        }

        var ticketId = Guid.NewGuid().ToString("N");
        var ticket = new ApprovalTicket
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            TenantId = context.TenantId,
            UserId = context.UserId,
            Risk = context.Risk ?? "medium",
            Reason = context.DecisionReason ?? "Approval required.",
            Status = "pending",
            IdempotencyKey = idempotencyKey,
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        await _approvalTicketRepository.AddAsync(ticket, cancellationToken).ConfigureAwait(false);

        context.Executed = false;
        context.ApprovalTicket = new ApprovalTicketDto
        {
            TicketId = ticketId,
            Risk = context.Risk ?? "medium",
            Reason = context.DecisionReason ?? "Approval required."
        };

        return await next().ConfigureAwait(false);
    }
}

public sealed class ActionMcpRegistryPolicyStep : IActionWorkflowStep
{
    private readonly McpExecutionOptions _options;
    private readonly IMcpRegistryClient _registryClient;
    private readonly IMcpPolicyClient _policyClient;
    private readonly McpPolicyEvaluator _policyEvaluator;

    public ActionMcpRegistryPolicyStep(
        McpExecutionOptions options,
        IMcpRegistryClient registryClient,
        IMcpPolicyClient policyClient,
        McpPolicyEvaluator policyEvaluator)
    {
        _options = options;
        _registryClient = registryClient;
        _policyClient = policyClient;
        _policyEvaluator = policyEvaluator;
    }

    public async Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (_options.IsDisabled)
        {
            context.McpPolicy = new McpPolicyEvaluation(true, false, true, "MCP execution is disabled by runtime mode.");
            context.McpPolicyBlocked = true;
            context.McpRequiresLedger = true;
            return await next().ConfigureAwait(false);
        }

        var registryResponse = await _registryClient.GetRegistryAsync(cancellationToken).ConfigureAwait(false);
        if (!registryResponse.IsSuccess || registryResponse.Data is null)
        {
            return ServiceResponse<ActionResponseDto>.Fail(
                registryResponse.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "MCP tool registry unavailable."),
                statusCode: registryResponse.StatusCode,
                message: registryResponse.Message);
        }

        var policyResponse = await _policyClient.GetPolicyAsync(cancellationToken).ConfigureAwait(false);
        if (!policyResponse.IsSuccess || policyResponse.Data is null)
        {
            return ServiceResponse<ActionResponseDto>.Fail(
                policyResponse.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "MCP policy unavailable."),
                statusCode: policyResponse.StatusCode,
                message: policyResponse.Message);
        }

        context.McpRegistryChecked = true;
        context.McpTool = registryResponse.Data.Tools.FirstOrDefault(tool =>
            string.Equals(tool.Name, context.ActionType, StringComparison.OrdinalIgnoreCase));
        context.McpPolicy = _policyEvaluator.Evaluate(
            context.ActionType,
            context.McpTool,
            policyResponse.Data,
            _options,
            TryResolveIntentConfidence(context.Payload));
        context.McpPolicyBlocked = context.McpPolicy.Blocked;
        context.McpRequiresApproval = context.McpPolicy.RequiresApproval;
        context.McpRequiresLedger = context.McpPolicy.RequiresLedger;

        return await next().ConfigureAwait(false);
    }

    private static double? TryResolveIntentConfidence(IReadOnlyDictionary<string, object?> payload)
    {
        return TryGetDouble(payload, "intent_confidence", out var confidence)
            || TryGetDouble(payload, "confidence", out confidence)
                ? Math.Max(0d, Math.Min(1d, confidence))
                : null;
    }

    private static bool TryGetDouble(
        IReadOnlyDictionary<string, object?> payload,
        string key,
        out double value)
    {
        value = 0d;
        if (!payload.TryGetValue(key, out var raw) || raw is null)
        {
            return false;
        }

        switch (raw)
        {
            case double doubleValue:
                value = doubleValue;
                return true;
            case float floatValue:
                value = floatValue;
                return true;
            case decimal decimalValue:
                value = (double)decimalValue;
                return true;
            case int intValue:
                value = intValue;
                return true;
            case long longValue:
                value = longValue;
                return true;
            case JsonElement element when element.ValueKind == JsonValueKind.Number:
                return element.TryGetDouble(out value);
            case JsonElement element when element.ValueKind == JsonValueKind.String:
                return double.TryParse(element.GetString(), out value);
            case string text:
                return double.TryParse(text, out value);
            default:
                return false;
        }
    }
}

public sealed class ActionMcpExecutionStep : IActionWorkflowStep
{
    private readonly McpExecutionOptions _options;
    private readonly IMcpToolExecutor _executor;
    private readonly McpActionLedgerRecorder _ledgerRecorder;

    public ActionMcpExecutionStep(
        McpExecutionOptions options,
        IMcpToolExecutor executor,
        McpActionLedgerRecorder ledgerRecorder)
    {
        _options = options;
        _executor = executor;
        _ledgerRecorder = ledgerRecorder;
    }

    public async Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (_options.IsDisabled && context.McpPolicy is null)
        {
            return await next().ConfigureAwait(false);
        }

        if (context.ApprovalTicket is not null || context.McpPolicyBlocked || !context.Executed)
        {
            await RecordIfRequiredAsync(context, cancellationToken).ConfigureAwait(false);
            return await next().ConfigureAwait(false);
        }

        if (context.McpTool is null)
        {
            context.Executed = false;
            context.Result = new Dictionary<string, object?>
            {
                ["status"] = "blocked",
                ["reason"] = "Tool is not registered in the MCP registry.",
                ["action_type"] = context.ActionType
            };
            await RecordIfRequiredAsync(context, cancellationToken).ConfigureAwait(false);
            return await next().ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(context.TraceId))
        {
            context.TraceId = Guid.NewGuid().ToString("N");
        }

        var execution = await _executor
            .ExecuteAsync(
                context.McpTool,
                context.TenantId,
                context.UserId,
                context.TraceId,
                context.Payload,
                cancellationToken)
            .ConfigureAwait(false);

        if (!execution.IsSuccess || execution.Data is null)
        {
            return ServiceResponse<ActionResponseDto>.Fail(
                execution.Error ?? new DomainError(ErrorCodes.DependencyUnavailable, "MCP execution failed."),
                statusCode: execution.StatusCode,
                message: execution.Message);
        }

        context.Executed = execution.Data.Executed;
        context.Result = execution.Data.Result;
        await RecordIfRequiredAsync(context, cancellationToken).ConfigureAwait(false);
        return await next().ConfigureAwait(false);
    }

    private async Task RecordIfRequiredAsync(ActionWorkflowContext context, CancellationToken cancellationToken)
    {
        if (!context.McpRequiresLedger && context.McpTool?.LogToLedger != true)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(context.TraceId))
        {
            context.TraceId = Guid.NewGuid().ToString("N");
        }

        var status = context.Result is not null && context.Result.TryGetValue("status", out var statusValue)
            ? statusValue?.ToString() ?? (context.Executed ? "executed" : "not_executed")
            : context.Executed ? "executed" : "not_executed";

        await _ledgerRecorder.RecordAsync(
            context.TraceId,
            context.TenantId,
            context.UserId,
            context.ActionType,
            context.McpTool,
            context.McpPolicy,
            context.Executed,
            status,
            context.Result,
            cancellationToken).ConfigureAwait(false);
    }
}

public sealed class ActionPolicyModeStep : IActionWorkflowStep
{
    private readonly PolicyDefaultsResolver _policyDefaultsResolver;

    public ActionPolicyModeStep(PolicyDefaultsResolver policyDefaultsResolver)
    {
        _policyDefaultsResolver = policyDefaultsResolver;
    }

    public async Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        var resolved = await _policyDefaultsResolver
            .ResolveAsync(context.TenantId, context.PolicyMode, packDocument: null, cancellationToken)
            .ConfigureAwait(false);

        if (!resolved.IsSuccess || resolved.Data is null)
        {
            return ServiceResponse<ActionResponseDto>.Fail(
                resolved.Error ?? new DomainError(ErrorCodes.InternalError, "Policy defaults unavailable."),
                statusCode: resolved.StatusCode,
                message: resolved.Message);
        }

        context.PolicyMode = resolved.Data;
        return await next().ConfigureAwait(false);
    }
}

public sealed class ActionSurvivalStatusStep : IActionWorkflowStep
{
    private readonly SurvivalService _survivalService;

    public ActionSurvivalStatusStep(SurvivalService survivalService)
    {
        _survivalService = survivalService;
    }

    public async Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        var status = await _survivalService.GetStatusAsync().ConfigureAwait(false);
        if (status.IsSuccess && status.Data is not null)
        {
            context.SurvivalMode = status.Data.Mode;
            context.SurvivalProfile = status.Data.Profile;
            context.SurvivalLastSignalUtc = status.Data.LastSignalUtc;
            context.SurvivalOfflineMessage = status.Data.OfflineMessage;
        }

        return await next().ConfigureAwait(false);
    }
}

public sealed class ActionResponseStep : IActionWorkflowStep
{
    private readonly Trace.TraceLookupStore _traceStore;

    public ActionResponseStep(Trace.TraceLookupStore traceStore)
    {
        _traceStore = traceStore;
    }

    public Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
        ActionWorkflowContext context,
        WorkflowStepDelegate<ActionResponseDto> next,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.TraceId))
        {
            context.TraceId = Guid.NewGuid().ToString("N");
        }

        Dictionary<string, object?>? resultPayload = null;
        if (context.Result is not null)
        {
            resultPayload = new Dictionary<string, object?>(context.Result, StringComparer.OrdinalIgnoreCase);
        }

        if (context.CorrectionMode is not null || context.Corrections.Count > 0)
        {
            resultPayload ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            if (context.CorrectionMode is not null)
            {
                resultPayload["correction_mode"] = context.CorrectionMode;
            }

            if (!string.IsNullOrWhiteSpace(context.CorrectionSummary))
            {
                resultPayload["correction_summary"] = context.CorrectionSummary;
            }

            if (context.Corrections.Count > 0)
            {
                resultPayload["corrections"] = context.Corrections.Select(correction => new Dictionary<string, object?>
                {
                    ["field"] = correction.Field,
                    ["from"] = correction.From,
                    ["to"] = correction.To,
                    ["note"] = correction.Note
                }).ToList();
            }
        }

        var sources = new List<string>();
        if (context.ApprovalTicket is not null)
        {
            sources.Add($"approval_ticket:{context.ApprovalTicket.TicketId}");
        }

        if (context.McpTool is not null)
        {
            sources.Add($"mcp_tool:{context.McpTool.Name}");
        }

        var response = new ActionResponseDto
        {
            Executed = context.Executed,
            ApprovalTicket = context.ApprovalTicket,
            Result = resultPayload,
            Trace = new TracePayload
            {
                TraceId = context.TraceId,
                Sources = sources,
                Confidence = 0.0f,
                PolicyMode = context.PolicyMode,
                Model = context.ModelName ?? string.Empty,
                PackHash = context.PackHash ?? string.Empty,
                PackVersion = context.PackVersion ?? string.Empty,
                PackCapabilities = Array.Empty<PackCapabilitySummary>(),
                Guardrails = context.GuardrailViolations,
                GuardrailsSummary = BuildGuardrailSummary(context),
                GuardrailsPointers = BuildGuardrailPointers(context),
                SurvivalMode = context.SurvivalMode,
                SurvivalProfile = context.SurvivalProfile,
                SurvivalLastSignalUtc = context.SurvivalLastSignalUtc,
                Corrections = context.Corrections
            }
        };

        if (!string.IsNullOrWhiteSpace(context.ActionType))
        {
            _traceStore.Record(context.TraceId, $"action:{context.ActionType}");
        }

        return Task.FromResult(ServiceResponse<ActionResponseDto>.Success(response));
    }

    private static GuardrailSummary? BuildGuardrailSummary(ActionWorkflowContext context)
    {
        if (context.GuardrailDiagnostics is null)
        {
            if (context.GuardrailViolations.Count == 0)
            {
                return null;
            }

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var violation in context.GuardrailViolations)
            {
                var key = violation.Severity?.Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
            }

            return new GuardrailSummary
            {
                Total = context.GuardrailViolations.Count,
                SeverityCounts = counts
            };
        }

        return new GuardrailSummary
        {
            Total = context.GuardrailDiagnostics.TotalViolations,
            SeverityCounts = context.GuardrailDiagnostics.SeverityCounts
        };
    }

    private static IReadOnlyList<string> BuildGuardrailPointers(ActionWorkflowContext context)
    {
        if (context.GuardrailDiagnostics is not null)
        {
            return context.GuardrailDiagnostics.ValidatorCounts.Keys.ToList();
        }

        if (context.GuardrailViolations.Count == 0)
        {
            return Array.Empty<string>();
        }

        return context.GuardrailViolations
            .Select(v => v.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
#nullable disable
