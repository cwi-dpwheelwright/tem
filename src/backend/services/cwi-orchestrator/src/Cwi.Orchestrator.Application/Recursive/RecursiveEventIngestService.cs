#nullable enable
using System.Text.Json;
using Cwi.Common;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Provenance;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Recursive;

public sealed class RecursiveEventIngestService : DomainServiceBase
{
    private readonly RecursiveEventSchemaValidator _validator;
    private readonly IRecursiveEventRepository _repository;
    private readonly ILedgerRepository _ledgerRepository;
    private readonly LedgerEventFactory _ledgerEventFactory;
    private readonly GraphEvolutionService? _graphEvolutionService;

    public RecursiveEventIngestService(
        RecursiveEventSchemaValidator validator,
        IRecursiveEventRepository repository,
        ILedgerRepository ledgerRepository,
        LedgerEventFactory ledgerEventFactory,
        GraphEvolutionService? graphEvolutionService = null)
    {
        _validator = validator;
        _repository = repository;
        _ledgerRepository = ledgerRepository;
        _ledgerEventFactory = ledgerEventFactory;
        _graphEvolutionService = graphEvolutionService;
    }

    public Task<ServiceResponse<RecursiveEventIngestResponseDto>> IngestAsync(
        string payloadJson,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync(async () =>
        {
            var extracted = RecursiveEventEnvelope.TryExtract(payloadJson);
            var validation = _validator.ValidateJson(payloadJson);
            if (!validation.IsSuccess)
            {
                return await QuarantineAsync(
                    extracted,
                    payloadJson,
                    "schema_validation_failed",
                    validation.Message,
                    cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(extracted.EventId))
            {
                return await QuarantineAsync(
                    extracted,
                    payloadJson,
                    "event_id_missing",
                    "Recursive event payload passed schema validation but event_id could not be extracted.",
                    cancellationToken).ConfigureAwait(false);
            }

            var exists = await _repository.ExistsAsync(extracted.EventId, cancellationToken).ConfigureAwait(false);
            if (exists)
            {
                return await QuarantineAsync(
                    extracted,
                    payloadJson,
                    "duplicate_event_id",
                    $"Recursive event {extracted.EventId} already exists.",
                    cancellationToken).ConfigureAwait(false);
            }

            var canonicalEventJson = CanonicalizeOrEncode(payloadJson);
            var record = extracted.ToRecord(canonicalEventJson);
            await _repository.AddAsync(record, cancellationToken).ConfigureAwait(false);
            var graphCandidateResult = _graphEvolutionService is null
                ? null
                : await _graphEvolutionService.CreateCandidateAsync(record, cancellationToken)
                    .ConfigureAwait(false);
            await RecordLedgerEventAsync(
                record.TraceId,
                "recursive_event.ingested",
                new
                {
                    event_id = record.EventId,
                    event_type = record.EventType,
                    tenant_id = record.TenantId,
                    trace_id = record.TraceId,
                    policy_status = record.PolicyStatus,
                    temporal_state = record.TemporalState,
                    confidence = record.Confidence,
                    source_component = record.SourceComponent,
                    source_type = record.SourceType,
                    source_hash = record.SourceHash,
                    graph_candidate_id = graphCandidateResult?.Candidate?.CandidateId,
                    graph_candidate_created = graphCandidateResult?.Created,
                    graph_candidate_skipped_reason = graphCandidateResult?.Reason,
                    recorded_utc = record.CreatedUtc
                },
                cancellationToken).ConfigureAwait(false);

            return ServiceResponse<RecursiveEventIngestResponseDto>.Success(
                new RecursiveEventIngestResponseDto
                {
                    Accepted = true,
                    Quarantined = false,
                    Status = "accepted",
                    EventId = record.EventId,
                    TraceId = record.TraceId,
                    TemporalState = record.TemporalState,
                    RecordedUtc = record.CreatedUtc
                },
                statusCode: 201,
                message: "Recursive event accepted.");
        });
    }

    public async Task<IReadOnlyList<RecursiveEventRecordDto>> QueryAsync(
        string? tenantId,
        string? eventType,
        string? temporalState,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Min(Math.Max(1, pageSize), 200);
        var records = await _repository.QueryAsync(
            string.IsNullOrWhiteSpace(tenantId) ? null : tenantId.Trim(),
            string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim(),
            string.IsNullOrWhiteSpace(temporalState) ? null : temporalState.Trim(),
            (normalizedPage - 1) * normalizedPageSize,
            normalizedPageSize,
            cancellationToken).ConfigureAwait(false);

        return records.Select(ToDto).ToArray();
    }

    private async Task<ServiceResponse<RecursiveEventIngestResponseDto>> QuarantineAsync(
        RecursiveEventEnvelope extracted,
        string payloadJson,
        string reason,
        string validationError,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var quarantineId = $"recursive-quarantine-{Guid.NewGuid():N}";
        var traceId = string.IsNullOrWhiteSpace(extracted.TraceId)
            ? quarantineId
            : extracted.TraceId!;
        var record = new RecursiveEventQuarantineRecord
        {
            Id = Guid.NewGuid(),
            QuarantineId = quarantineId,
            EventId = extracted.EventId,
            TenantId = extracted.TenantId,
            TraceId = traceId,
            Reason = reason,
            Status = "quarantined",
            PayloadJson = CanonicalizeOrEncode(payloadJson),
            ValidationError = validationError,
            CreatedUtc = now
        };

        await _repository.AddQuarantineAsync(record, cancellationToken).ConfigureAwait(false);
        await RecordLedgerEventAsync(
            traceId,
            "recursive_event.quarantined",
            new
            {
                quarantine_id = record.QuarantineId,
                event_id = record.EventId,
                tenant_id = record.TenantId,
                trace_id = record.TraceId,
                reason = record.Reason,
                validation_error = record.ValidationError,
                recorded_utc = record.CreatedUtc
            },
            cancellationToken).ConfigureAwait(false);

        return ServiceResponse<RecursiveEventIngestResponseDto>.Success(
            new RecursiveEventIngestResponseDto
            {
                Accepted = false,
                Quarantined = true,
                Status = "quarantined",
                EventId = record.EventId,
                QuarantineId = record.QuarantineId,
                Reason = record.Reason,
                TraceId = record.TraceId,
                RecordedUtc = record.CreatedUtc
            },
            statusCode: 202,
            message: "Recursive event quarantined.");
    }

    private async Task RecordLedgerEventAsync(
        string traceId,
        string eventType,
        object payload,
        CancellationToken cancellationToken)
    {
        var payloadJson = JsonSerializer.Serialize(payload);
        using var document = JsonDocument.Parse(payloadJson);
        var canonicalPayload = CanonicalJson.Canonicalize(document.RootElement);
        var prevHash = await _ledgerRepository.GetLatestHashAsync(traceId, cancellationToken)
            .ConfigureAwait(false);
        var ledgerEvent = _ledgerEventFactory.CreateSignedEvent(
            traceId,
            eventType,
            canonicalPayload,
            prevHash ?? string.Empty);
        await _ledgerRepository.AppendAsync(ledgerEvent, cancellationToken).ConfigureAwait(false);
    }

    private static string CanonicalizeOrEncode(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return "null";
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return CanonicalJson.Canonicalize(document.RootElement);
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(payloadJson);
        }
    }

    private static RecursiveEventRecordDto ToDto(RecursiveEventRecord record)
    {
        return new RecursiveEventRecordDto
        {
            EventId = record.EventId,
            EventType = record.EventType,
            TenantId = record.TenantId,
            ScopeType = record.ScopeType,
            SourceComponent = record.SourceComponent,
            SourceType = record.SourceType,
            Confidence = record.Confidence,
            TraceId = record.TraceId,
            PolicyStatus = record.PolicyStatus,
            TemporalState = record.TemporalState,
            TimestampUtc = record.TimestampUtc,
            CreatedUtc = record.CreatedUtc
        };
    }

    private sealed class RecursiveEventEnvelope
    {
        public string? EventId { get; init; }
        public string? SchemaVersion { get; init; }
        public string? EventType { get; init; }
        public string? TenantId { get; init; }
        public string? ScopeType { get; init; }
        public string? UserId { get; init; }
        public string? UserIdHash { get; init; }
        public string? NodeId { get; init; }
        public string? SourceComponent { get; init; }
        public string? SourceType { get; init; }
        public string? SourceId { get; init; }
        public string? SourceNodeId { get; init; }
        public double Confidence { get; init; }
        public string? TraceId { get; init; }
        public string? PolicyStatus { get; init; }
        public string? TemporalState { get; init; }
        public DateTimeOffset TimestampUtc { get; init; }
        public string PayloadJson { get; init; } = "{}";
        public string ProvenanceJson { get; init; } = "{}";
        public string? SourceHash { get; init; }

        public RecursiveEventRecord ToRecord(string canonicalEventJson)
        {
            return new RecursiveEventRecord
            {
                Id = Guid.NewGuid(),
                EventId = EventId ?? string.Empty,
                SchemaVersion = SchemaVersion ?? string.Empty,
                EventType = EventType ?? string.Empty,
                TenantId = TenantId ?? string.Empty,
                ScopeType = ScopeType ?? string.Empty,
                UserId = UserId,
                UserIdHash = UserIdHash,
                NodeId = NodeId,
                SourceComponent = SourceComponent ?? string.Empty,
                SourceType = SourceType ?? string.Empty,
                SourceId = SourceId,
                SourceNodeId = SourceNodeId,
                Confidence = Confidence,
                TraceId = TraceId ?? string.Empty,
                PolicyStatus = PolicyStatus ?? string.Empty,
                TemporalState = TemporalState ?? string.Empty,
                TimestampUtc = TimestampUtc,
                PayloadJson = PayloadJson,
                ProvenanceJson = ProvenanceJson,
                EventJson = canonicalEventJson,
                SourceHash = SourceHash ?? string.Empty,
                CreatedUtc = DateTimeOffset.UtcNow
            };
        }

        public static RecursiveEventEnvelope TryExtract(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return new RecursiveEventEnvelope();
            }

            try
            {
                using var document = JsonDocument.Parse(payloadJson);
                var root = document.RootElement;
                var userScope = TryGetObject(root, "user_scope");
                var source = TryGetObject(root, "source");
                var provenance = TryGetObject(root, "provenance");
                return new RecursiveEventEnvelope
                {
                    EventId = GetString(root, "event_id"),
                    SchemaVersion = GetString(root, "schema_version"),
                    EventType = GetString(root, "event_type"),
                    TenantId = GetString(root, "tenant_id"),
                    ScopeType = userScope.HasValue ? GetString(userScope.Value, "scope_type") : null,
                    UserId = userScope.HasValue ? GetString(userScope.Value, "user_id") : null,
                    UserIdHash = userScope.HasValue ? GetString(userScope.Value, "user_id_hash") : null,
                    NodeId = userScope.HasValue ? GetString(userScope.Value, "node_id") : null,
                    SourceComponent = source.HasValue ? GetString(source.Value, "component") : null,
                    SourceType = source.HasValue ? GetString(source.Value, "source_type") : null,
                    SourceId = source.HasValue ? GetString(source.Value, "source_id") : null,
                    SourceNodeId = source.HasValue ? GetString(source.Value, "node_id") : null,
                    Confidence = GetDouble(root, "confidence"),
                    TraceId = GetString(root, "trace_id"),
                    PolicyStatus = GetString(root, "policy_status"),
                    TemporalState = GetString(root, "temporal_state"),
                    TimestampUtc = GetDateTimeOffset(root, "timestamp_utc"),
                    PayloadJson = TryGetObject(root, "payload") is { } payload ? CanonicalJson.Canonicalize(payload) : "{}",
                    ProvenanceJson = provenance.HasValue ? CanonicalJson.Canonicalize(provenance.Value) : "{}",
                    SourceHash = provenance.HasValue ? GetString(provenance.Value, "source_hash") : null
                };
            }
            catch (JsonException)
            {
                return new RecursiveEventEnvelope();
            }
        }

        private static JsonElement? TryGetObject(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Object
                ? value
                : null;
        }

        private static string? GetString(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim()
                : null;
        }

        private static double GetDouble(JsonElement root, string propertyName)
        {
            return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
                ? value.GetDouble()
                : 0.0d;
        }

        private static DateTimeOffset GetDateTimeOffset(JsonElement root, string propertyName)
        {
            if (root.TryGetProperty(propertyName, out var value)
                && value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), out var parsed))
            {
                return parsed.ToUniversalTime();
            }

            return DateTimeOffset.MinValue;
        }
    }
}
#nullable disable
