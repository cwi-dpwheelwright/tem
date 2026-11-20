#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Application.Recursive;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Runtime;

public sealed class RuntimeCriticLedgerRecorder
{
    public const string EventType = "runtime_critic.observed";

    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _factory;
    private readonly RecursiveEventIngestService? _recursiveEventIngestService;

    public RuntimeCriticLedgerRecorder(
        ILedgerRepository ledgerRepository,
        LedgerEventFactory factory,
        RecursiveEventIngestService? recursiveEventIngestService = null)
    {
        _ledgerRepository = ledgerRepository;
        _factory = factory;
        _recursiveEventIngestService = recursiveEventIngestService;
    }

    public async Task RecordAsync(
        AskWorkflowContext context,
        AskResponseDto response,
        RuntimeCriticResult result,
        SelectiveEnforcementMode enforcementMode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.TraceId))
        {
            return;
        }

        var previousHash = await _ledgerRepository
            .GetLatestHashAsync(context.TraceId, cancellationToken)
            .ConfigureAwait(false) ?? string.Empty;

        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["event"] = "RUNTIME_CRITIC_OBSERVED",
            ["version"] = result.Version,
            ["mode"] = result.Mode,
            ["enforcement_mode"] = enforcementMode.ToString().ToLowerInvariant(),
            ["tenant_id"] = context.TenantId,
            ["user_id"] = context.UserId,
            ["request_id"] = context.RequestId,
            ["trace_id"] = context.TraceId,
            ["route"] = response.Trace.Route,
            ["execution_state"] = response.ExecutionState,
            ["execution_target"] = response.ExecutionTarget,
            ["model"] = response.Trace.Model,
            ["pack_hash"] = response.Trace.PackHash,
            ["answer_hash"] = context.FinalAnswerHash,
            ["confidence"] = response.Confidence,
            ["overall_score"] = result.OverallScore,
            ["scores"] = result.Scores,
            ["findings"] = result.Findings
        });

        var ledgerEvent = _factory.CreateSignedEvent(
            context.TraceId,
            EventType,
            payload,
            previousHash);

        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);

        if (_recursiveEventIngestService is null || result.Findings.Count == 0)
        {
            return;
        }

        for (var index = 0; index < result.Findings.Count; index++)
        {
            var eventJson = BuildQualityObservationEventJson(
                context,
                response,
                result,
                result.Findings[index],
                index);
            await _recursiveEventIngestService.IngestAsync(eventJson, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildQualityObservationEventJson(
        AskWorkflowContext context,
        AskResponseDto response,
        RuntimeCriticResult result,
        RuntimeCriticFinding finding,
        int index)
    {
        var now = DateTimeOffset.UtcNow;
        var route = response.Trace.Route ?? context.Route ?? "unknown";
        var promptType = FirstNonEmpty(
            context.IntentType,
            context.PresentationTemplateFamily,
            context.ChatResponseProfile,
            context.QueryClassification,
            "unknown");
        var sourceBehavior = result.Scores
            .FirstOrDefault(score => string.Equals(score.Dimension, "source_quality", StringComparison.OrdinalIgnoreCase))
            ?.Reason ?? "unknown";
        var sourceHash = HashText(string.Join(
            "|",
            context.TraceId,
            context.RequestId,
            context.FinalAnswerHash,
            finding.Category,
            finding.Rule,
            finding.Severity,
            result.OverallScore.ToString("0.###")));

        var evt = new
        {
            schema_version = "recursive-event.v1",
            event_id = $"quality-{HashText($"{context.TraceId}|{context.RequestId}|{finding.Category}|{finding.Rule}|{index}")}",
            event_type = "quality_observation.created",
            tenant_id = context.TenantId,
            user_scope = new
            {
                scope_type = "user",
                user_id_hash = HashUserId(context.UserId)
            },
            source = new
            {
                component = "runtime_critic",
                source_type = "critic",
                source_id = context.TraceId
            },
            confidence = ConfidenceForFinding(finding),
            trace_id = context.TraceId,
            policy_status = result.Mode,
            timestamp_utc = now,
            temporal_state = "current",
            payload = new
            {
                category = finding.Category,
                rule = finding.Rule,
                severity = finding.Severity,
                detail = finding.Detail,
                route,
                prompt_type = promptType,
                source_behavior = sourceBehavior,
                critic_score = result.OverallScore,
                fix_status = "active",
                execution_state = response.ExecutionState,
                execution_target = response.ExecutionTarget
            },
            provenance = new
            {
                source_hash = sourceHash,
                created_utc = now,
                actor = "cwi-orchestrator",
                parent_event_ids = Array.Empty<string>()
            }
        };

        return JsonSerializer.Serialize(evt);
    }

    private static double ConfidenceForFinding(RuntimeCriticFinding finding)
    {
        return finding.Severity.Trim().ToLowerInvariant() switch
        {
            "fail" => 0.95d,
            "warn" => 0.8d,
            _ => 0.6d
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "unknown";
    }

    private static string HashUserId(string? userId)
    {
        return HashText(string.IsNullOrWhiteSpace(userId) ? "unknown" : userId.Trim());
    }

    private static string HashText(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
#nullable disable
