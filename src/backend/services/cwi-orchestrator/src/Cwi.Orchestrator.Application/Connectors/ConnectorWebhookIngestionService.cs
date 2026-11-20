#nullable enable
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Etl;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;

namespace Cwi.Orchestrator.Application.Connectors;

public sealed class ConnectorWebhookIngestionService
{
    private static readonly Regex ConnectorTypeRegex = new("^[a-z0-9][a-z0-9_-]{1,63}$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IngestDocumentService _ingestDocumentService;
    private readonly ISourceMemoryRepository _sourceMemoryRepository;
    private readonly IConnectorIngestionRepository _connectorIngestionRepository;
    private readonly ConnectorNormalizationService _normalizationService;

    public ConnectorWebhookIngestionService(
        IngestDocumentService ingestDocumentService,
        ISourceMemoryRepository sourceMemoryRepository,
        IConnectorIngestionRepository connectorIngestionRepository,
        ConnectorNormalizationService normalizationService)
    {
        _ingestDocumentService = ingestDocumentService;
        _sourceMemoryRepository = sourceMemoryRepository;
        _connectorIngestionRepository = connectorIngestionRepository;
        _normalizationService = normalizationService;
    }

    public async Task<ServiceResponse<ConnectorWebhookResponseDto>> IngestAsync(
        string connectorType,
        string tenantId,
        string? userId,
        ConnectorWebhookRequestDto request,
        CancellationToken cancellationToken)
    {
        var normalizedType = NormalizeConnectorType(connectorType);
        if (normalizedType is null)
        {
            return ServiceResponse<ConnectorWebhookResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "connector type is invalid."),
                statusCode: 400,
                message: "connector type is invalid");
        }

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResponse<ConnectorWebhookResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "tenant_id is required."),
                statusCode: 400,
                message: "tenant_id is required");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResponse<ConnectorWebhookResponseDto>.Fail(
                new DomainError(ErrorCodes.InvalidRequest, "user_id is required."),
                statusCode: 400,
                message: "user_id is required");
        }

        var payload = ResolvePayload(request);
        var receivedUtc = DateTimeOffset.UtcNow;
        var normalization = _normalizationService.Normalize(
            normalizedType,
            tenantId.Trim(),
            userId.Trim(),
            request,
            payload,
            receivedUtc);

        var receipts = new List<ConnectorIngestionReceiptDto>();
        var validationErrorReceipt = normalization.Errors.Count > 0
            ? BuildMalformedReceipt(
                normalizedType,
                tenantId.Trim(),
                userId.Trim(),
                string.Join("; ", normalization.Errors.Distinct()),
                receivedUtc)
            : null;
        if (validationErrorReceipt is not null)
        {
            await PersistReceiptAsync(validationErrorReceipt, cancellationToken).ConfigureAwait(false);
            receipts.Add(validationErrorReceipt);
        }

        if (normalization.Errors.Count > 0)
        {
            if (normalization.Records.Count == 0)
            {
                return ServiceResponse<ConnectorWebhookResponseDto>.Fail(
                    new DomainError(ErrorCodes.InvalidRequest, "MALFORMED_PAYLOAD: " + string.Join("; ", normalization.Errors.Distinct())),
                    statusCode: 400,
                    message: "MALFORMED_PAYLOAD");
            }
        }

        var acceptedRecordIds = new List<string>();
        string? firstDocId = null;
        string? firstTraceId = null;
        var replayed = 0;
        var accepted = 0;

        foreach (var record in normalization.Records)
        {
            var sourceSha = ConnectorNormalizationService.SourceSha256For(record);
            var existingRecord = await _connectorIngestionRepository
                .GetRecordAsync(record.TenantId!, record.RecordId!, record.UserId!, cancellationToken)
                .ConfigureAwait(false);
            var existingSource = await _sourceMemoryRepository
                .GetSourceAsync(record.TenantId!, record.RecordId!, record.UserId!, cancellationToken)
                .ConfigureAwait(false);
            var duplicateReplay = existingRecord is not null
                && string.Equals(existingRecord.SourceSha256, sourceSha, StringComparison.OrdinalIgnoreCase)
                && existingSource is not null
                && record.SyncState?.IsUpdate != true;

            await _connectorIngestionRepository
                .UpsertRecordAsync(ToEntity(record, sourceSha, receivedUtc), cancellationToken)
                .ConfigureAwait(false);

            var factKey = ConnectorNormalizationService.ComputeFactKey(record);
            var memoryId = $"memory:{factKey[..24]}";
            var existingFact = await _connectorIngestionRepository
                .GetMemoryFactAsync(record.TenantId!, factKey, record.UserId!, cancellationToken)
                .ConfigureAwait(false);
            await _connectorIngestionRepository
                .UpsertMemoryFactAsync(ToFactIndex(record, factKey, memoryId, existingFact, receivedUtc), cancellationToken)
                .ConfigureAwait(false);

            IngestDocumentResponseDto? ingestData = null;
            if (!duplicateReplay && record.SyncState?.IsDeleted != true)
            {
                var ingestResult = await _ingestDocumentService
                    .HandleAsync(BuildIngestRequest(record, sourceSha), cancellationToken)
                    .ConfigureAwait(false);

                if (!ingestResult.IsSuccess || ingestResult.Data is null)
                {
                    var failedReceipt = BuildReceipt(
                        record,
                        "FAILED",
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        Array.Empty<string>(),
                        ingestResult.Message,
                        receivedUtc);
                    await PersistReceiptAsync(failedReceipt, cancellationToken).ConfigureAwait(false);
                    receipts.Add(failedReceipt);
                    continue;
                }

                ingestData = ingestResult.Data;
                firstDocId ??= ingestData.DocId;
                firstTraceId ??= ingestData.TraceId;
                accepted += 1;
            }
            else
            {
                replayed += 1;
            }

            var status = duplicateReplay
                ? "SKIPPED_DUPLICATE"
                : record.SyncState?.IsDeleted == true
                    ? "DELETE_OBSERVED"
                    : "INGESTED";
            var receipt = BuildReceipt(
                record,
                status,
                existingFact is null && !duplicateReplay ? new[] { memoryId } : Array.Empty<string>(),
                existingFact is not null || duplicateReplay ? new[] { memoryId } : Array.Empty<string>(),
                ExpectedGraphEdges(record, memoryId),
                null,
                receivedUtc);
            await PersistReceiptAsync(receipt, cancellationToken).ConfigureAwait(false);
            receipts.Add(receipt);
            acceptedRecordIds.Add(record.RecordId!);
        }

        return ServiceResponse<ConnectorWebhookResponseDto>.Success(new ConnectorWebhookResponseDto
        {
            Accepted = receipts.Any(receipt => receipt.Status is "INGESTED" or "SKIPPED_DUPLICATE" or "DELETE_OBSERVED"),
            ConnectorType = normalizedType,
            NormalizedItems = normalization.Records.Count,
            RecordsAccepted = accepted,
            RecordsReplayed = replayed,
            RecordsRejected = receipts.Count(receipt => receipt.Status is "FAILED" or "MALFORMED_PAYLOAD"),
            RecordIds = acceptedRecordIds,
            Receipts = receipts,
            DocId = firstDocId ?? acceptedRecordIds.FirstOrDefault(),
            TraceId = firstTraceId,
            Message = receipts.Any(receipt => receipt.Status is "FAILED" or "MALFORMED_PAYLOAD") ? "partial_success" : "accepted"
        });
    }

    private async Task PersistReceiptAsync(ConnectorIngestionReceiptDto receipt, CancellationToken cancellationToken)
    {
        await _connectorIngestionRepository.AddReceiptAsync(new ConnectorIngestionReceipt
        {
            Id = Guid.NewGuid(),
            ReceiptId = receipt.ReceiptId,
            RecordId = receipt.RecordId,
            TenantId = receipt.TenantId,
            UserId = receipt.UserId,
            ConnectorType = receipt.ConnectorType,
            Status = receipt.Status,
            MemoryIdsCreatedJson = JsonSerializer.Serialize(receipt.MemoryIdsCreated, JsonOptions),
            MemoryIdsUpdatedJson = JsonSerializer.Serialize(receipt.MemoryIdsUpdated, JsonOptions),
            GraphEdgesCreatedJson = JsonSerializer.Serialize(receipt.GraphEdgesCreated, JsonOptions),
            Error = receipt.Error,
            CreatedUtc = receipt.CreatedAt
        }, cancellationToken).ConfigureAwait(false);
    }

    private static IngestDocumentRequestDto BuildIngestRequest(NormalizedConnectorRecordDto record, string sourceSha)
    {
        var stale = ConnectorNormalizationService.IsStale(record);
        return new IngestDocumentRequestDto
        {
            Version = ConnectorNormalizationService.SchemaVersion,
            TenantId = record.TenantId!,
            UserId = record.UserId,
            IdempotencyKey = record.RecordId,
            Tags = new List<string>
            {
                "connector_normalized",
                "connector_normalized_v1",
                $"connector:{record.ConnectorType}",
                $"life_work_scope:{record.LifeWorkScope}",
                $"sensitivity:{record.SensitivityLabel}"
            },
            Document = new IngestDocumentPayloadDto
            {
                DocId = record.RecordId!,
                Title = record.Title!,
                MimeType = "application/json",
                Content = BuildNormalizedContent(record),
                ContentEncoding = "plain",
                Source = $"connector_normalized:{record.ConnectorType}",
                CreatedUtc = record.EventTime,
                Metadata = BuildMetadata(record, sourceSha, stale)
            }
        };
    }

    private static Dictionary<string, object?> BuildMetadata(
        NormalizedConnectorRecordDto record,
        string sourceSha,
        bool stale)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["source_type"] = "connector_normalized",
            ["connector_type"] = record.ConnectorType,
            ["connector_domain"] = record.LifeWorkScope,
            ["source_system"] = record.SourceSystem,
            ["account_id"] = record.AccountId,
            ["source_record_id"] = record.SourceRecordId,
            ["source_thread_id"] = record.SourceThreadId,
            ["source_url"] = record.SourceUrl,
            ["event_time"] = record.EventTime?.UtcDateTime.ToString("O"),
            ["ingested_at"] = record.IngestedAt?.UtcDateTime.ToString("O"),
            ["normalized_items"] = 1,
            ["normalization_mode"] = "connector_normalized_v1",
            ["schema_version"] = ConnectorNormalizationService.SchemaVersion,
            ["storage_location"] = "orchestrator_source_memory",
            ["extraction_method"] = "connector_normalized_contract",
            ["record_id"] = record.RecordId,
            ["life_work_scope"] = record.LifeWorkScope,
            ["sensitivity_label"] = record.SensitivityLabel,
            ["confidence"] = record.Confidence,
            ["source_sha256"] = sourceSha,
            ["candidate_memory_domains"] = string.Join(",", record.CandidateMemoryDomains),
            ["candidate_memory_types"] = string.Join(",", record.CandidateMemoryTypes),
            ["stale"] = stale,
            ["is_deleted"] = record.SyncState?.IsDeleted ?? false,
            ["is_update"] = record.SyncState?.IsUpdate ?? false
        };
    }

    private static string BuildNormalizedContent(NormalizedConnectorRecordDto record)
    {
        var lines = new List<string>
        {
            $"schema_version: {ConnectorNormalizationService.SchemaVersion}",
            $"record_id: {record.RecordId}",
            $"connector_type: {record.ConnectorType}",
            $"source_system: {record.SourceSystem}",
            $"event_time: {record.EventTime:O}",
            $"life_work_scope: {record.LifeWorkScope}",
            $"sensitivity_label: {record.SensitivityLabel}",
            $"confidence: {record.Confidence:0.###}",
            "title:",
            record.Title ?? string.Empty,
            "body:",
            record.Body ?? string.Empty,
            "normalized_record_json:",
            ConnectorNormalizationService.SerializeRecord(record)
        };

        return string.Join('\n', lines);
    }

    private static NormalizedConnectorRecordEntity ToEntity(
        NormalizedConnectorRecordDto record,
        string sourceSha,
        DateTimeOffset now)
    {
        return new NormalizedConnectorRecordEntity
        {
            Id = Guid.NewGuid(),
            RecordId = record.RecordId!,
            TenantId = record.TenantId!,
            UserId = record.UserId!,
            AccountId = record.AccountId!,
            ConnectorType = record.ConnectorType!,
            SourceSystem = record.SourceSystem!,
            SourceRecordId = record.SourceRecordId!,
            SourceThreadId = record.SourceThreadId,
            SourceUrl = record.SourceUrl,
            EventTime = record.EventTime!.Value,
            IngestedAt = record.IngestedAt!.Value,
            Title = record.Title!,
            Body = record.Body!,
            RawExcerpt = record.RawExcerpt,
            LifeWorkScope = record.LifeWorkScope!,
            SensitivityLabel = record.SensitivityLabel!,
            Confidence = record.Confidence!.Value,
            SourceSha256 = sourceSha,
            RecordJson = ConnectorNormalizationService.SerializeRecord(record),
            ProvenanceJson = JsonSerializer.Serialize(record.Provenance, JsonOptions),
            SyncStateJson = JsonSerializer.Serialize(record.SyncState, JsonOptions),
            IsDeleted = record.SyncState?.IsDeleted ?? false,
            IsUpdate = record.SyncState?.IsUpdate ?? false,
            Stale = ConnectorNormalizationService.IsStale(record),
            CreatedUtc = now,
            UpdatedUtc = now,
            LastSeenUtc = now,
            ReplayCount = 0
        };
    }

    private static MemoryFactIndexRecord ToFactIndex(
        NormalizedConnectorRecordDto record,
        string factKey,
        string memoryId,
        MemoryFactIndexRecord? existing,
        DateTimeOffset now)
    {
        var recordIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (existing is not null)
        {
            try
            {
                foreach (var id in JsonSerializer.Deserialize<string[]>(existing.RecordIdsJson) ?? Array.Empty<string>())
                {
                    recordIds.Add(id);
                }
            }
            catch (JsonException)
            {
                // Invalid legacy fact index JSON should not block idempotent connector ingest.
            }
        }

        recordIds.Add(record.RecordId!);
        return new MemoryFactIndexRecord
        {
            Id = existing?.Id ?? Guid.NewGuid(),
            FactKey = factKey,
            TenantId = record.TenantId!,
            UserId = record.UserId!,
            LifeWorkScope = record.LifeWorkScope!,
            MemoryDomain = record.CandidateMemoryDomains.FirstOrDefault() ?? "general",
            MemoryType = record.CandidateMemoryTypes.FirstOrDefault() ?? "knowledge_memory",
            NormalizedFactText = NormalizeFactText(record.Body!),
            RecordIdsJson = JsonSerializer.Serialize(recordIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase), JsonOptions),
            Confidence = record.Confidence!.Value,
            CreatedUtc = existing?.CreatedUtc ?? now,
            UpdatedUtc = now,
            LastSeenUtc = now
        };
    }

    private static ConnectorIngestionReceiptDto BuildReceipt(
        NormalizedConnectorRecordDto record,
        string status,
        IReadOnlyList<string> memoryIdsCreated,
        IReadOnlyList<string> memoryIdsUpdated,
        IReadOnlyList<string> graphEdgesCreated,
        string? error,
        DateTimeOffset now)
    {
        return new ConnectorIngestionReceiptDto
        {
            ReceiptId = Guid.NewGuid().ToString("N"),
            RecordId = record.RecordId!,
            TenantId = record.TenantId!,
            UserId = record.UserId!,
            ConnectorType = record.ConnectorType!,
            Status = status,
            MemoryIdsCreated = memoryIdsCreated,
            MemoryIdsUpdated = memoryIdsUpdated,
            GraphEdgesCreated = graphEdgesCreated,
            Error = error,
            CreatedAt = now
        };
    }

    private static ConnectorIngestionReceiptDto BuildMalformedReceipt(
        string connectorType,
        string tenantId,
        string userId,
        string error,
        DateTimeOffset now)
    {
        return new ConnectorIngestionReceiptDto
        {
            ReceiptId = Guid.NewGuid().ToString("N"),
            RecordId = $"malformed:{HashText(tenantId, userId, connectorType, error, now.UtcDateTime.ToString("O"))[..24]}",
            TenantId = tenantId,
            UserId = userId,
            ConnectorType = connectorType,
            Status = "MALFORMED_PAYLOAD",
            MemoryIdsCreated = Array.Empty<string>(),
            MemoryIdsUpdated = Array.Empty<string>(),
            GraphEdgesCreated = Array.Empty<string>(),
            Error = error,
            CreatedAt = now
        };
    }

    private static IReadOnlyList<string> ExpectedGraphEdges(NormalizedConnectorRecordDto record, string memoryId)
    {
        var sourceNodeId = $"source_record:{record.RecordId}";
        var versionNodeId = $"memory_version:{HashText(record.RecordId, record.Provenance?.SourceSha256)}";
        var domainNodeId = $"domain:{record.CandidateMemoryDomains.FirstOrDefault() ?? "general"}";
        return new[]
        {
            $"edge:{HashText(sourceNodeId, memoryId, "PRODUCED")[..24]}",
            $"edge:{HashText(memoryId, versionNodeId, "CURRENT_VERSION")[..24]}",
            $"edge:{HashText(versionNodeId, sourceNodeId, "EVIDENCED_BY")[..24]}",
            $"edge:{HashText(memoryId, domainNodeId, "BELONGS_TO_DOMAIN")[..24]}"
        };
    }

    private static JsonElement ResolvePayload(ConnectorWebhookRequestDto request)
    {
        if (request.Payload is { } payload && payload.ValueKind is not JsonValueKind.Undefined)
        {
            return payload;
        }

        if (request.Items is { } items && items.ValueKind is not JsonValueKind.Undefined)
        {
            return items;
        }

        var fallback = new Dictionary<string, object?>
        {
            ["event_type"] = request.EventType,
            ["account_id"] = request.AccountId,
            ["source_system"] = request.SourceSystem,
            ["source_record_id"] = request.SourceId,
            ["metadata"] = RedactMetadata(request.Metadata)
        };
        return JsonSerializer.SerializeToElement(fallback, JsonOptions);
    }

    private static Dictionary<string, object?> RedactMetadata(Dictionary<string, object?> metadata)
    {
        return metadata.ToDictionary(
            entry => entry.Key,
            entry => ConnectorSensitiveFieldPolicy.IsSensitiveKey(entry.Key) ? "[redacted]" : entry.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private static string? NormalizeConnectorType(string connectorType)
    {
        var normalized = connectorType.Trim().ToLowerInvariant()
            .Replace('/', '_')
            .Replace('-', '_')
            .Replace(' ', '_');
        normalized = Regex.Replace(normalized, "_+", "_").Trim('_');
        return ConnectorTypeRegex.IsMatch(normalized) ? normalized : null;
    }

    private static string NormalizeFactText(string value)
    {
        return string.Join(' ', value.Trim().ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string HashText(params string?[] parts)
    {
        var joined = string.Join("\n", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
#nullable disable
