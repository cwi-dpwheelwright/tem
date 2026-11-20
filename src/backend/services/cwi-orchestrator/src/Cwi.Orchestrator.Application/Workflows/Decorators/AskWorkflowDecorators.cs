#nullable enable
using Cwi.Common;
using Cwi.Orchestrator.Application.Guardrails.Provenance;
using Cwi.Orchestrator.Application.Guardrails.Scoring;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Application.Packs.Policy;
using Cwi.Orchestrator.Application.Packs.Provenance;
using Cwi.Orchestrator.Application.Runtime;
using Cwi.Orchestrator.Application.Survival.Provenance;
using Cwi.Orchestrator.Application.Telemetry;
using Microsoft.Extensions.Logging;

namespace Cwi.Orchestrator.Application.Workflows.Decorators;

public sealed class AskIdempotencyDecorator : IAskWorkflowDecorator
{
    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        return next();
    }
}

public sealed class AskKillSwitchDecorator : IAskWorkflowDecorator
{
    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        return next();
    }
}

public sealed class AskProvenanceDecorator : IAskWorkflowDecorator
{
    private readonly IGuardrailLedgerRecorder _ledgerRecorder;
    private readonly SeverityScorer _scorer;

    public AskProvenanceDecorator(IGuardrailLedgerRecorder ledgerRecorder, SeverityScorer scorer)
    {
        _ledgerRecorder = ledgerRecorder;
        _scorer = scorer;
    }

    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        return ExecuteInternalAsync(context, next, cancellationToken);
    }

    private async Task<ServiceResponse<AskResponseDto>> ExecuteInternalAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);
        if (!response.IsSuccess || context.TraceId is null)
        {
            return response;
        }

        if (context.GuardrailViolations.Count == 0)
        {
            return response;
        }

        var score = _scorer.Score(context.GuardrailViolations);
        await _ledgerRecorder.RecordAsync(
            context.TraceId,
            flow: "ask",
            policyMode: context.PolicyMode,
            actionType: null,
            modelId: context.ModelName,
            packHash: context.PackHash,
            packVersion: context.PackVersion,
            policyVersion: context.PolicyVersion,
            violations: context.GuardrailViolations,
            score: score,
            decision: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return response;
    }
}

public sealed class AskSurvivalLedgerDecorator : IAskWorkflowDecorator
{
    private readonly SurvivalLedgerRecorder _ledgerRecorder;

    public AskSurvivalLedgerDecorator(SurvivalLedgerRecorder ledgerRecorder)
    {
        _ledgerRecorder = ledgerRecorder;
    }

    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        return ExecuteInternalAsync(context, next, cancellationToken);
    }

    private async Task<ServiceResponse<AskResponseDto>> ExecuteInternalAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);
        if (!response.IsSuccess || context.TraceId is null)
        {
            return response;
        }

        await _ledgerRecorder
            .RecordTraceAsync(
                context.TraceId,
                context.SurvivalMode,
                context.SurvivalProfile,
                context.SurvivalLastSignalUtc,
                cancellationToken)
            .ConfigureAwait(false);

        return response;
    }
}

public sealed class AskPackCapabilityLedgerDecorator : IAskWorkflowDecorator
{
    private readonly IPackCapabilityLedgerRecorder _ledgerRecorder;

    public AskPackCapabilityLedgerDecorator(IPackCapabilityLedgerRecorder ledgerRecorder)
    {
        _ledgerRecorder = ledgerRecorder;
    }

    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        return ExecuteInternalAsync(context, next, cancellationToken);
    }

    private async Task<ServiceResponse<AskResponseDto>> ExecuteInternalAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);
        if (context.PackCapabilityDecision is null || context.TraceId is null)
        {
            return response;
        }

        var summaries = BuildPackCapabilitySummary(context.PackCapabilities);
        await _ledgerRecorder.RecordAsync(
            context.TraceId,
            context.PackName,
            context.PackHash ?? string.Empty,
            context.PackVersion ?? string.Empty,
            context.PolicyMode,
            context.ModelName,
            context.PolicyVersion,
            summaries,
            context.PackCapabilityDecision,
            cancellationToken).ConfigureAwait(false);

        return response;
    }

    private static IReadOnlyList<PackCapabilitySummary> BuildPackCapabilitySummary(PackCapabilityMap? capabilityMap)
    {
        if (capabilityMap is null || capabilityMap.Capabilities.Count == 0)
        {
            return Array.Empty<PackCapabilitySummary>();
        }

        return capabilityMap.Capabilities.Select(capability => new PackCapabilitySummary
        {
            Name = capability.Name,
            Scope = capability.Scope,
            Risk = capability.Risk?.ToString().ToLowerInvariant(),
            RequiresApproval = capability.RequiresApproval
        }).ToList();
    }
}

public sealed class AskMetricsDecorator : IAskWorkflowDecorator
{
    private readonly SustainabilityMetricsService? _sustainabilityMetrics;
    private readonly ILogger<AskMetricsDecorator>? _logger;

    public AskMetricsDecorator(
        SustainabilityMetricsService? sustainabilityMetrics = null,
        ILogger<AskMetricsDecorator>? logger = null)
    {
        _sustainabilityMetrics = sustainabilityMetrics;
        _logger = logger;
    }

    public async Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var started = DateTimeOffset.UtcNow;
        var response = await next().ConfigureAwait(false);
        if (!response.IsSuccess || response.Data is null || _sustainabilityMetrics is null)
        {
            return response;
        }

        try
        {
            await _sustainabilityMetrics
                .RecordAskAsync(context, response.Data, DateTimeOffset.UtcNow - started, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Sustainability metrics recording failed for trace {TraceId}.", context.TraceId);
        }

        return response;
    }
}

public sealed class AskRuntimeCriticDecorator : IAskWorkflowDecorator
{
    private readonly IRuntimeCriticClient _criticClient;
    private readonly RuntimeCriticLedgerRecorder _ledgerRecorder;
    private readonly SelectiveEnforcementOptions _options;
    private readonly ILogger<AskRuntimeCriticDecorator> _logger;

    public AskRuntimeCriticDecorator(
        IRuntimeCriticClient criticClient,
        RuntimeCriticLedgerRecorder ledgerRecorder,
        SelectiveEnforcementOptions options,
        ILogger<AskRuntimeCriticDecorator> logger)
    {
        _criticClient = criticClient;
        _ledgerRecorder = ledgerRecorder;
        _options = options;
        _logger = logger;
    }

    public Task<ServiceResponse<AskResponseDto>> ExecuteAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        return ExecuteInternalAsync(context, next, cancellationToken);
    }

    private async Task<ServiceResponse<AskResponseDto>> ExecuteInternalAsync(
        AskWorkflowContext context,
        WorkflowStepDelegate<AskResponseDto> next,
        CancellationToken cancellationToken)
    {
        var response = await next().ConfigureAwait(false);
        if (!response.IsSuccess
            || response.Data is null
            || string.IsNullOrWhiteSpace(context.TraceId)
            || !_options.IsEnabled(SelectiveEnforcementFeature.Critic))
        {
            return response;
        }

        try
        {
            var result = await _criticClient
                .EvaluateAsync(context, response.Data, cancellationToken)
                .ConfigureAwait(false);
            if (result is null)
            {
                return response;
            }

            await _ledgerRecorder.RecordAsync(
                context,
                response.Data,
                result,
                _options.Resolve(SelectiveEnforcementFeature.Critic),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Runtime critic observe-only recording failed for trace {TraceId}.", context.TraceId);
        }

        return response;
    }
}
#nullable disable
