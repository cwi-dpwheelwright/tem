using System.Text.Json;
using Cwi.Orchestrator.Application.Connectors;
using Cwi.Orchestrator.Application.Documents;
using Cwi.Orchestrator.Application.Etl;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Trace;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Connectors;

public sealed class ConnectorWebhookIngestionServiceTests
{
    [Fact]
    public async Task IngestAsyncNormalizesWebhookPayloadIntoSourceMemory()
    {
        var repository = new InMemorySourceMemoryRepository();
        var ingestService = new IngestDocumentService(
            new IngestDocumentSchemaValidator(),
            new TraceLookupStore(),
            new DocumentStore(),
            repository);
        var connectorRepository = new InMemoryConnectorIngestionRepository();
        var service = new ConnectorWebhookIngestionService(
            ingestService,
            repository,
            connectorRepository,
            new ConnectorNormalizationService());
        using var payloadDoc = JsonDocument.Parse("""
            {
              "subject": "Deadline risk",
              "body": "Need to follow up with the school and investor demo this week.",
              "token": "secret-token"
            }
            """);

        var result = await service.IngestAsync(
            "email",
            "tenant-1",
            "user-1",
            new ConnectorWebhookRequestDto
            {
                EventType = "message.created",
                Payload = payloadDoc.RootElement.Clone()
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Accepted.Should().BeTrue();
        result.Data.ConnectorType.Should().Be("email");
        result.Data.RecordIds.Should().ContainSingle();
        result.Data.Receipts.Should().ContainSingle(receipt => receipt.Status == "INGESTED");
        connectorRepository.Records.Should().ContainSingle(record => record.RecordJson.Contains(ConnectorNormalizationService.SchemaVersion, StringComparison.Ordinal));
        connectorRepository.Receipts.Should().ContainSingle(receipt => receipt.Status == "INGESTED");
        repository.Sources.Should().ContainSingle(source => source.SourceType == "connector_normalized");
        repository.Chunks.Should().ContainSingle();
        repository.Chunks[0].ChunkText.Should().Contain("title:");
        repository.Chunks[0].ChunkText.Should().Contain("Deadline risk");
        repository.Chunks[0].ChunkText.Should().Contain("Need to follow up");
        repository.Chunks[0].ChunkText.Should().NotContain("secret-token");
    }

    [Fact]
    public async Task IngestAsyncRejectsInvalidConnectorType()
    {
        var repository = new InMemorySourceMemoryRepository();
        var ingestService = new IngestDocumentService(
            new IngestDocumentSchemaValidator(),
            new TraceLookupStore(),
            new DocumentStore(),
            repository);
        var service = new ConnectorWebhookIngestionService(
            ingestService,
            repository,
            new InMemoryConnectorIngestionRepository(),
            new ConnectorNormalizationService());

        var result = await service.IngestAsync(
            "../bad",
            "tenant-1",
            "user-1",
            new ConnectorWebhookRequestDto(),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task IngestAsyncReplaysDuplicateWithoutCreatingSourceAgain()
    {
        var repository = new InMemorySourceMemoryRepository();
        var connectorRepository = new InMemoryConnectorIngestionRepository();
        var ingestService = new IngestDocumentService(
            new IngestDocumentSchemaValidator(),
            new TraceLookupStore(),
            new DocumentStore(),
            repository);
        var service = new ConnectorWebhookIngestionService(
            ingestService,
            repository,
            connectorRepository,
            new ConnectorNormalizationService());
        using var payloadDoc = JsonDocument.Parse("""
            {
              "id": "msg-1",
              "subject": "Client deadline",
              "body": "Jordan Smith emailed: Please follow up by Friday on the client deadline."
            }
            """);
        var request = new ConnectorWebhookRequestDto
        {
            AccountId = "acct-1",
            SourceSystem = "outlook",
            SourceId = "msg-1",
            OccurredUtc = DateTimeOffset.Parse("2026-04-29T15:00:00Z"),
            Payload = payloadDoc.RootElement.Clone()
        };

        var first = await service.IngestAsync("email", "tenant-1", "user-1", request, CancellationToken.None);
        var second = await service.IngestAsync("email", "tenant-1", "user-1", request, CancellationToken.None);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        repository.Sources.Should().ContainSingle();
        repository.Chunks.Should().ContainSingle();
        connectorRepository.Records.Should().ContainSingle();
        connectorRepository.Receipts.Should().HaveCount(2);
        second.Data!.Receipts.Should().ContainSingle(receipt => receipt.Status == "SKIPPED_DUPLICATE");
        second.Data.RecordsReplayed.Should().Be(1);
    }

    [Fact]
    public async Task IngestAsyncCommitsValidRecordsWhenBatchContainsMalformedRecord()
    {
        var repository = new InMemorySourceMemoryRepository();
        var connectorRepository = new InMemoryConnectorIngestionRepository();
        var ingestService = new IngestDocumentService(
            new IngestDocumentSchemaValidator(),
            new TraceLookupStore(),
            new DocumentStore(),
            repository);
        var service = new ConnectorWebhookIngestionService(
            ingestService,
            repository,
            connectorRepository,
            new ConnectorNormalizationService());

        var result = await service.IngestAsync(
            "email",
            "tenant-1",
            "user-1",
            new ConnectorWebhookRequestDto
            {
                AccountId = "acct-1",
                SourceSystem = "outlook",
                NormalizedRecords = new[]
                {
                    new NormalizedConnectorRecordDto
                    {
                        RecordId = "record-good",
                        ConnectorType = "email",
                        SourceRecordId = "msg-good",
                        EventTime = DateTimeOffset.Parse("2026-04-29T15:00:00Z"),
                        Title = "Client deadline",
                        Body = "Jordan Smith emailed: Please follow up by Friday on the client deadline."
                    },
                    new NormalizedConnectorRecordDto
                    {
                        SchemaVersion = "bad-schema",
                        RecordId = "record-bad",
                        ConnectorType = "email",
                        SourceRecordId = "msg-bad",
                        EventTime = DateTimeOffset.Parse("2026-04-29T15:01:00Z"),
                        Title = "Malformed",
                        Body = "This record should be rejected."
                    }
                }
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Accepted.Should().BeTrue();
        result.Data.Message.Should().Be("partial_success");
        result.Data.RecordsAccepted.Should().Be(1);
        result.Data.RecordsRejected.Should().Be(1);
        result.Data.Receipts.Should().Contain(receipt => receipt.Status == "INGESTED");
        result.Data.Receipts.Should().Contain(receipt => receipt.Status == "MALFORMED_PAYLOAD");
        repository.Sources.Should().ContainSingle();
        connectorRepository.Records.Should().ContainSingle(record => record.RecordId == "record-good");
        connectorRepository.Receipts.Should().Contain(receipt => receipt.Status == "MALFORMED_PAYLOAD");
    }

    [Fact]
    public async Task IngestAsyncAssignsRestrictedFinancialLabelForBilling()
    {
        var repository = new InMemorySourceMemoryRepository();
        var connectorRepository = new InMemoryConnectorIngestionRepository();
        var ingestService = new IngestDocumentService(
            new IngestDocumentSchemaValidator(),
            new TraceLookupStore(),
            new DocumentStore(),
            repository);
        var service = new ConnectorWebhookIngestionService(
            ingestService,
            repository,
            connectorRepository,
            new ConnectorNormalizationService());
        using var payloadDoc = JsonDocument.Parse("""
            {
              "id": "bill-1",
              "title": "Car insurance bill",
              "body": "Car insurance bill due Friday for $180."
            }
            """);

        var result = await service.IngestAsync(
            "billing",
            "tenant-1",
            "user-1",
            new ConnectorWebhookRequestDto
            {
                AccountId = "acct-1",
                SourceSystem = "billing",
                SourceId = "bill-1",
                Payload = payloadDoc.RootElement.Clone()
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        connectorRepository.Records.Should().ContainSingle(record =>
            record.SensitivityLabel == "restricted_financial" && record.LifeWorkScope == "personal");
        repository.Chunks[0].ChunkText.Should().Contain("sensitivity_label: restricted_financial");
    }

    private sealed class InMemorySourceMemoryRepository : ISourceMemoryRepository
    {
        public List<SourceMemoryRecord> Sources { get; } = new();

        public List<SourceChunkRecord> Chunks { get; } = new();

        public Task UpsertSourceAsync(SourceMemoryRecord source, IReadOnlyList<SourceChunkRecord> chunks, CancellationToken cancellationToken)
        {
            Sources.RemoveAll(item => item.SourceId == source.SourceId);
            Sources.Add(source);
            Chunks.RemoveAll(item => item.SourceId == source.SourceId);
            Chunks.AddRange(chunks);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SourceChunkRecord>> ListChunkCandidatesAsync(
            string tenantId,
            string userId,
            string? nodeId,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SourceChunkRecord>>(Chunks.Take(limit).ToArray());
        }

        public Task<IReadOnlyList<SourceMemoryRecord>> ListSourcesAsync(
            string tenantId,
            string? userId,
            string? sourceType,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SourceMemoryRecord>>(Sources.Take(limit).ToArray());
        }

        public Task<SourceMemoryRecord?> GetSourceAsync(
            string tenantId,
            string sourceId,
            string? userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Sources.FirstOrDefault(source => source.SourceId == sourceId));
        }

        public Task<IReadOnlyList<SourceChunkRecord>> ListChunksForSourceAsync(
            string tenantId,
            string sourceId,
            string? userId,
            int limit,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<SourceChunkRecord>>(
                Chunks.Where(chunk => chunk.SourceId == sourceId).Take(limit).ToArray());
        }
    }

    private sealed class InMemoryConnectorIngestionRepository : IConnectorIngestionRepository
    {
        public List<NormalizedConnectorRecordEntity> Records { get; } = new();

        public List<MemoryFactIndexRecord> Facts { get; } = new();

        public List<ConnectorIngestionReceipt> Receipts { get; } = new();

        public Task<NormalizedConnectorRecordEntity?> GetRecordAsync(
            string tenantId,
            string recordId,
            string userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Records.FirstOrDefault(record =>
                record.TenantId == tenantId && record.UserId == userId && record.RecordId == recordId));
        }

        public Task UpsertRecordAsync(
            NormalizedConnectorRecordEntity record,
            CancellationToken cancellationToken)
        {
            var existing = Records.FirstOrDefault(item =>
                item.TenantId == record.TenantId && item.UserId == record.UserId && item.RecordId == record.RecordId);
            if (existing is null)
            {
                Records.Add(record);
            }
            else
            {
                existing.SourceSha256 = record.SourceSha256;
                existing.SensitivityLabel = record.SensitivityLabel;
                existing.LifeWorkScope = record.LifeWorkScope;
                existing.ReplayCount += 1;
            }

            return Task.CompletedTask;
        }

        public Task<MemoryFactIndexRecord?> GetMemoryFactAsync(
            string tenantId,
            string factKey,
            string userId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Facts.FirstOrDefault(fact =>
                fact.TenantId == tenantId && fact.UserId == userId && fact.FactKey == factKey));
        }

        public Task UpsertMemoryFactAsync(
            MemoryFactIndexRecord fact,
            CancellationToken cancellationToken)
        {
            Facts.RemoveAll(item => item.TenantId == fact.TenantId && item.UserId == fact.UserId && item.FactKey == fact.FactKey);
            Facts.Add(fact);
            return Task.CompletedTask;
        }

        public Task AddReceiptAsync(
            ConnectorIngestionReceipt receipt,
            CancellationToken cancellationToken)
        {
            Receipts.Add(receipt);
            return Task.CompletedTask;
        }
    }
}
