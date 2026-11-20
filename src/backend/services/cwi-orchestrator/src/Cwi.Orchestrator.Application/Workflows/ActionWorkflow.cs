#nullable enable
using System.Collections.Generic;
using Cwi.Orchestrator.Application.Models;

namespace Cwi.Orchestrator.Application.Workflows;

public sealed class ActionWorkflowContext
{
    public ActionWorkflowContext(
        string tenantId,
        string userId,
        string policyMode,
        string actionType,
        IReadOnlyDictionary<string, object?> payload)
    {
        TenantId = tenantId;
        UserId = userId;
        PolicyMode = policyMode;
        ActionType = actionType;
        Payload = new Dictionary<string, object?>(payload, StringComparer.OrdinalIgnoreCase);
    }

    public string TenantId { get; }
    public string UserId { get; }
    public string PolicyMode { get; set; }
    public string ActionType { get; }
    public Dictionary<string, object?> Payload { get; set; }

    public bool Executed { get; set; }
    public ApprovalTicketDto? ApprovalTicket { get; set; }
    public Dictionary<string, object?>? Result { get; set; }
    public string? TraceId { get; set; }
    public string? Risk { get; set; }
    public string? DecisionReason { get; set; }
    public string? GuardrailSeverity { get; set; }
    public IReadOnlyList<GuardrailViolation> GuardrailViolations { get; set; } = Array.Empty<GuardrailViolation>();
    public Guardrails.Governance.GuardrailDecision? GuardrailDecision { get; set; }
    public Guardrails.Diagnostics.GuardrailDiagnostics? GuardrailDiagnostics { get; set; }
    public IReadOnlyList<CorrectionDiff> Corrections { get; set; } = Array.Empty<CorrectionDiff>();
    public string? CorrectionMode { get; set; }
    public string? CorrectionSummary { get; set; }
    public Simulation.SimulationFeasibilityResult? SimulationFeasibility { get; set; }
    public Guardrails.Governance.GuardrailDecision? SimulationDecision { get; set; }
    public string? ModelName { get; set; }
    public string? PackHash { get; set; }
    public string? PackVersion { get; set; }
    public string? PolicyVersion { get; set; }
    public string? SurvivalMode { get; set; }
    public string? SurvivalProfile { get; set; }
    public DateTimeOffset? SurvivalLastSignalUtc { get; set; }
    public string? SurvivalOfflineMessage { get; set; }
    public Mcp.McpToolDefinition? McpTool { get; set; }
    public Mcp.McpPolicyEvaluation? McpPolicy { get; set; }
    public bool McpRegistryChecked { get; set; }
    public bool McpPolicyBlocked { get; set; }
    public bool McpRequiresApproval { get; set; }
    public bool McpRequiresLedger { get; set; }
}

public interface IActionWorkflowStep : IWorkflowStep<ActionWorkflowContext, ActionResponseDto>
{
}

public interface IActionWorkflowDecorator : IWorkflowDecorator<ActionWorkflowContext, ActionResponseDto>
{
}

public sealed class ActionWorkflowPipeline : WorkflowPipeline<ActionWorkflowContext, ActionResponseDto>
{
    public ActionWorkflowPipeline(
        IEnumerable<IActionWorkflowStep> steps,
        IEnumerable<IActionWorkflowDecorator> decorators)
        : base(steps, decorators)
    {
    }
}
#nullable disable
