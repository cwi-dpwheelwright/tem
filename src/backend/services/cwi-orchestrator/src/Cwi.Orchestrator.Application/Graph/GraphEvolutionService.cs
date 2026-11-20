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
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Graph;

public sealed class GraphEvolutionService
{
    private static readonly HashSet<string> CandidateStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "add",
        "update",
        "supersede",
        "conflict"
    };

    private readonly IGraphUpdateCandidateRepository _repository;
    private readonly GraphToVectorRefreshService? _refreshService;

    public GraphEvolutionService(
        IGraphUpdateCandidateRepository repository,
        GraphToVectorRefreshService? refreshService = null)
    {
        _repository = repository;
        _refreshService = refreshService;
    }

    public async Task<GraphEvolutionCreateResultDto> CreateCandidateAsync(
        RecursiveEventRecord recursiveEvent,
        CancellationToken cancellationToken)
    {
        if (!IsGraphEligibleEvent(recursiveEvent))
        {
            return new GraphEvolutionCreateResultDto
            {
                Created = false,
                Skipped = true,
                Reason = "event_type_not_graph_eligible"
            };
        }

        if (await _repository.ExistsForRecursiveEventAsync(recursiveEvent.EventId, cancellationToken)
                .ConfigureAwait(false))
        {
            return new GraphEvolutionCreateResultDto
            {
                Created = false,
                Skipped = true,
                Reason = "candidate_already_exists"
            };
        }

        var projection = GraphFactProjection.FromEvent(recursiveEvent);
        var now = DateTimeOffset.UtcNow;
        var record = new GraphUpdateCandidateRecord
        {
            Id = Guid.NewGuid(),
            CandidateId = BuildCandidateId(recursiveEvent, projection),
            TenantId = recursiveEvent.TenantId,
            UserIdHash = recursiveEvent.UserIdHash,
            NodeId = recursiveEvent.NodeId ?? recursiveEvent.SourceNodeId,
            RecursiveEventId = recursiveEvent.EventId,
            TraceId = recursiveEvent.TraceId,
            CandidateState = projection.CandidateState,
            TemporalState = NormalizeTemporalState(recursiveEvent.TemporalState),
            Subject = projection.Subject,
            Relation = projection.Relation,
            Object = projection.Object,
            FactKey = projection.FactKey,
            Confidence = recursiveEvent.Confidence,
            ProvenanceJson = recursiveEvent.ProvenanceJson,
            PayloadJson = recursiveEvent.PayloadJson,
            SourceComponent = recursiveEvent.SourceComponent,
            SourceHash = recursiveEvent.SourceHash,
            ObservedUtc = recursiveEvent.TimestampUtc,
            CreatedUtc = now,
            UpdatedUtc = now
        };

        await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
        var refreshJob = _refreshService is null
            ? null
            : await _refreshService.CreateRefreshJobAsync(record, cancellationToken).ConfigureAwait(false);
        return new GraphEvolutionCreateResultDto
        {
            Created = true,
            Skipped = false,
            Candidate = ToDto(record),
            VectorRefreshJob = refreshJob?.Job
        };
    }

    public async Task<IReadOnlyList<GraphUpdateCandidateDto>> QueryAsync(
        string? tenantId,
        string? candidateState,
        string? temporalState,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Min(Math.Max(1, pageSize), 200);
        var records = await _repository.QueryAsync(
            NormalizeOptional(tenantId),
            NormalizeOptional(candidateState),
            NormalizeOptional(temporalState),
            (normalizedPage - 1) * normalizedPageSize,
            normalizedPageSize,
            cancellationToken).ConfigureAwait(false);

        return records.Select(ToDto).ToArray();
    }

    private static bool IsGraphEligibleEvent(RecursiveEventRecord record)
    {
        return string.Equals(record.EventType, "memory_candidate.observed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(record.EventType, "graph_fact.observed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(record.EventType, "graph_update.proposed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(record.SourceType, "process_miner", StringComparison.OrdinalIgnoreCase)
               || string.Equals(record.SourceType, "graph", StringComparison.OrdinalIgnoreCase);
    }

    private static GraphUpdateCandidateDto ToDto(GraphUpdateCandidateRecord record)
    {
        return new GraphUpdateCandidateDto
        {
            CandidateId = record.CandidateId,
            TenantId = record.TenantId,
            UserIdHash = record.UserIdHash,
            NodeId = record.NodeId,
            RecursiveEventId = record.RecursiveEventId,
            TraceId = record.TraceId,
            CandidateState = record.CandidateState,
            TemporalState = record.TemporalState,
            Subject = record.Subject,
            Relation = record.Relation,
            Object = record.Object,
            FactKey = record.FactKey,
            Confidence = record.Confidence,
            SourceComponent = record.SourceComponent,
            SourceHash = record.SourceHash,
            ObservedUtc = record.ObservedUtc,
            CreatedUtc = record.CreatedUtc
        };
    }

    private static string BuildCandidateId(
        RecursiveEventRecord recursiveEvent,
        GraphFactProjection projection)
    {
        return $"graph-candidate-{HashText(string.Join(
            "|",
            recursiveEvent.TenantId,
            recursiveEvent.EventId,
            projection.CandidateState,
            projection.FactKey))[..24]}";
    }

    private static string NormalizeTemporalState(string? temporalState)
    {
        return string.IsNullOrWhiteSpace(temporalState)
            ? "current"
            : temporalState.Trim().ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string HashText(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private sealed class GraphFactProjection
    {
        public string CandidateState { get; init; } = "add";
        public string Subject { get; init; } = string.Empty;
        public string Relation { get; init; } = "observed";
        public string Object { get; init; } = string.Empty;
        public string FactKey { get; init; } = string.Empty;

        public static GraphFactProjection FromEvent(RecursiveEventRecord record)
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(record.PayloadJson) ? "{}" : record.PayloadJson);
            var root = document.RootElement;
            var subject = FirstNonEmpty(
                GetString(root, "subject"),
                GetString(root, "slot"),
                GetString(root, "category"),
                record.EventType);
            var relation = FirstNonEmpty(
                GetString(root, "relation"),
                GetString(root, "predicate"),
                GetString(root, "memory_type"),
                "observed");
            var obj = FirstNonEmpty(
                GetString(root, "object"),
                GetString(root, "value"),
                GetString(root, "summary"),
                GetString(root, "rule"),
                record.EventId);
            var candidateState = ResolveCandidateState(record, root);
            var factKey = FirstNonEmpty(
                GetString(root, "fact_key"),
                HashText($"{record.TenantId}|{subject}|{relation}|{obj}")[..32]);

            return new GraphFactProjection
            {
                CandidateState = candidateState,
                Subject = subject,
                Relation = relation,
                Object = obj,
                FactKey = factKey
            };
        }

        private static string ResolveCandidateState(RecursiveEventRecord record, JsonElement root)
        {
            var explicitState = FirstNonEmpty(
                GetString(root, "candidate_state"),
                GetString(root, "graph_state"),
                GetString(root, "operation"));
            if (CandidateStates.Contains(explicitState))
            {
                return explicitState.Trim().ToLowerInvariant();
            }

            if (string.Equals(record.TemporalState, "conflicted", StringComparison.OrdinalIgnoreCase)
                || ContainsAny(record.PayloadJson, "conflict", "contradict", "disagree"))
            {
                return "conflict";
            }

            if (root.TryGetProperty("supersedes_event_id", out _)
                || root.TryGetProperty("supersedes_fact_key", out _))
            {
                return "supersede";
            }

            if (record.EventType.Contains("updated", StringComparison.OrdinalIgnoreCase))
            {
                return "update";
            }

            return "add";
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

            return string.Empty;
        }

        private static string? GetString(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static bool ContainsAny(string haystack, params string[] needles)
        {
            return needles.Any(needle => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase));
        }
    }
}
#nullable disable
