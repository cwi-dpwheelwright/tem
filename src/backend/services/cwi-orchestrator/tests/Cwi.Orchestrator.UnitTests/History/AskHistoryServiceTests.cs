using Cwi.Orchestrator.Application.History;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.History;

public sealed class AskHistoryServiceTests
{
    [Fact]
    public async Task RecordAsync_allows_processing_record_without_answer()
    {
        var service = new AskHistoryService(new InMemoryAskHistoryRepository());

        var response = await service.RecordAsync(
            "tenant-1",
            "user-1",
            new AskHistoryRecordRequestDto
            {
                RequestId = "req-processing",
                Question = "Why is this slow?",
                Answer = "",
                ResponseStatus = "processing"
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.ResponseStatus.Should().Be("processing");
        response.Data.Answer.Should().BeEmpty();
    }

    [Fact]
    public async Task RecordAsync_updates_existing_processing_record_by_request_id()
    {
        var repository = new InMemoryAskHistoryRepository();
        var service = new AskHistoryService(repository);

        await service.RecordAsync(
            "tenant-1",
            "user-1",
            new AskHistoryRecordRequestDto
            {
                RequestId = "req-shared",
                Question = "What is CWI?",
                Answer = "",
                ResponseStatus = "processing"
            },
            CancellationToken.None);

        var completed = await service.RecordAsync(
            "tenant-1",
            "user-1",
            new AskHistoryRecordRequestDto
            {
                RequestId = "req-shared",
                Question = "What is CWI?",
                Answer = "CWI stands for Christian Wheelwright Intelligence.",
                ResponseStatus = "completed",
                TraceId = "trace-shared"
            },
            CancellationToken.None);

        completed.IsSuccess.Should().BeTrue();
        completed.Data!.ResponseStatus.Should().Be("completed");
        completed.Data.Answer.Should().Contain("Christian Wheelwright Intelligence");

        var listed = await service.ListAsync("tenant-1", "user-1", limit: 10, retentionDays: 0, CancellationToken.None);
        listed.IsSuccess.Should().BeTrue();
        listed.Data!.Items.Should().ContainSingle();
        listed.Data.Items[0].RequestId.Should().Be("req-shared");
        listed.Data.Items[0].ResponseStatus.Should().Be("completed");
    }

    private sealed class InMemoryAskHistoryRepository : IAskHistoryRepository
    {
        private readonly List<AskHistoryRecord> _records = new();

        public Task AddAsync(AskHistoryRecord record, CancellationToken cancellationToken)
        {
            var existing = _records.FirstOrDefault(candidate =>
                !string.IsNullOrWhiteSpace(record.RequestId) &&
                candidate.TenantId == record.TenantId &&
                candidate.UserId == record.UserId &&
                candidate.RequestId == record.RequestId);
            if (existing is null)
            {
                _records.Add(record);
                return Task.CompletedTask;
            }

            existing.ConversationId = record.ConversationId;
            existing.Question = record.Question;
            existing.Answer = record.Answer;
            existing.TraceId = record.TraceId;
            existing.ProvenanceId = record.ProvenanceId;
            existing.PackName = record.PackName;
            existing.PackHash = record.PackHash;
            existing.ContentSha256 = record.ContentSha256;
            existing.SummaryStatus = record.SummaryStatus;
            existing.TraceJson = record.TraceJson;
            existing.AttachmentsJson = record.AttachmentsJson;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AskHistoryRecord>> ListAsync(
            string tenantId,
            string userId,
            int limit,
            DateTimeOffset? createdAfter,
            CancellationToken cancellationToken)
        {
            var records = _records
                .Where(record => record.TenantId == tenantId && record.UserId == userId)
                .Where(record => createdAfter is null || record.CreatedUtc >= createdAfter.Value)
                .OrderByDescending(record => record.CreatedUtc)
                .Take(limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<AskHistoryRecord>>(records);
        }

        public Task<int> DeleteOlderThanAsync(
            string tenantId,
            string userId,
            DateTimeOffset cutoff,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_records.RemoveAll(record =>
                record.TenantId == tenantId &&
                record.UserId == userId &&
                record.CreatedUtc < cutoff));
        }

        public Task<int> DeleteAllAsync(string tenantId, string userId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_records.RemoveAll(record => record.TenantId == tenantId && record.UserId == userId));
        }

        public Task<int> TrimToLatestAsync(
            string tenantId,
            string userId,
            int maxEntries,
            CancellationToken cancellationToken)
        {
            var keep = _records
                .Where(record => record.TenantId == tenantId && record.UserId == userId)
                .OrderByDescending(record => record.CreatedUtc)
                .Take(maxEntries)
                .Select(record => record.Id)
                .ToHashSet();
            return Task.FromResult(_records.RemoveAll(record =>
                record.TenantId == tenantId &&
                record.UserId == userId &&
                !keep.Contains(record.Id)));
        }
    }
}
