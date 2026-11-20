using Cwi.Orchestrator.Application.Ownership;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Ownership;

public sealed class OwnershipGraphServiceTests
{
    [Fact]
    public async Task GetLineageAsync_builds_nodes_and_edges()
    {
        var repository = new InMemoryOwnershipTransferRepository(new List<OwnershipTransferRequest>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RequestId = "req-1",
                TenantId = "tenant-1",
                FromUserId = "owner-1",
                ToUserId = "owner-2",
                TransferType = "transfer",
                Status = "approved",
                Reason = "scope:Billing; ownership transfer",
                UpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
            },
            new()
            {
                Id = Guid.NewGuid(),
                RequestId = "req-2",
                TenantId = "tenant-1",
                FromUserId = "owner-1",
                ToUserId = "delegate-1",
                TransferType = "delegate",
                Status = "pending",
                Reason = "scope:Ops; delegation",
                UpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        });

        var service = new OwnershipGraphService(repository);

        var result = await service.GetLineageAsync("tenant-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Edges.Should().Contain(edge =>
            edge.From == "owner-1"
            && edge.To == "owner-2"
            && edge.Relationship == "successor"
            && edge.Scope == "Billing");
        result.Data.Edges.Should().Contain(edge =>
            edge.From == "owner-1"
            && edge.To == "delegate-1"
            && edge.Relationship == "delegate"
            && edge.Scope == "Ops");
        result.Data.Nodes.Should().Contain(node =>
            node.NodeId == "owner-2"
            && node.Role == "Successor"
            && node.Status == "active");
        result.Data.Nodes.Should().Contain(node =>
            node.NodeId == "delegate-1"
            && node.Role == "Delegate"
            && node.Status == "pending");
    }

    [Fact]
    public async Task GetRevocationsAsync_maps_delegate_statuses()
    {
        var repository = new InMemoryOwnershipTransferRepository(new List<OwnershipTransferRequest>
        {
            new()
            {
                Id = Guid.NewGuid(),
                RequestId = "req-1",
                TenantId = "tenant-1",
                FromUserId = "owner-1",
                ToUserId = "delegate-1",
                TransferType = "delegate",
                Status = "revoked",
                Reason = "scope:Ops; revoked",
                UpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
            },
            new()
            {
                Id = Guid.NewGuid(),
                RequestId = "req-2",
                TenantId = "tenant-1",
                FromUserId = "owner-1",
                ToUserId = "delegate-2",
                TransferType = "delegate",
                Status = "approved",
                Reason = "scope:Ops; approved",
                UpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            }
        });

        var service = new OwnershipGraphService(repository);

        var result = await service.GetRevocationsAsync("tenant-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Contain(entry =>
            entry.RequestId == "req-1"
            && entry.Status == "revoked"
            && entry.Scope == "Ops");
        result.Data.Should().Contain(entry =>
            entry.RequestId == "req-2"
            && entry.Status == "confirmed"
            && entry.Scope == "Ops");
    }

    private sealed class InMemoryOwnershipTransferRepository : IOwnershipTransferRepository
    {
        private readonly List<OwnershipTransferRequest> _records;

        public InMemoryOwnershipTransferRepository(List<OwnershipTransferRequest> records)
        {
            _records = records;
        }

        public Task<OwnershipTransferRequest?> GetByRequestIdAsync(string requestId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_records.FirstOrDefault(x => x.RequestId == requestId));
        }

        public Task<OwnershipTransferRequest?> GetByApprovalTicketIdAsync(string approvalTicketId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_records.FirstOrDefault(x => x.ApprovalTicketId == approvalTicketId));
        }

        public Task<OwnershipTransferRequest?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(_records.FirstOrDefault(x => x.IdempotencyKey == idempotencyKey));
        }

        public Task<IReadOnlyList<OwnershipTransferRequest>> ListByTenantAsync(string tenantId, CancellationToken cancellationToken)
        {
            var results = _records.Where(x => x.TenantId == tenantId).ToList();
            return Task.FromResult<IReadOnlyList<OwnershipTransferRequest>>(results);
        }

        public Task AddAsync(OwnershipTransferRequest request, CancellationToken cancellationToken)
        {
            _records.Add(request);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(OwnershipTransferRequest request, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
