using System.Text.Json;
using Cwi.Coordination.Application;
using Cwi.Coordination.Application.Models;
using Cwi.Coordination.Application.Services;
using Cwi.Coordination.Application.Transport;
using Cwi.Coordination.DataAccess;
using Cwi.Coordination.Domain.Entities;
using FluentAssertions;

namespace Cwi.Coordination.UnitTests;

public sealed class NodeWorkServiceTests
{
    [Fact]
    public async Task CreateAsync_Pushes_Work_Immediately_When_Node_Session_Is_Active()
    {
        var membershipRepository = new InMemoryMembershipRepository();
        await membershipRepository.AddAsync(new Member
        {
            NodeId = "node-home-1",
            TenantId = "tenant-1",
            OwnerUserId = "user-1",
            CertificateFingerprint = "fp-node-home-1",
            Status = MembershipStatus.Active
        }, CancellationToken.None);

        var dispatcher = new CapturingNodeSessionDispatcher();
        var workRepository = new InMemoryNodeWorkRepository();
        var ledgerRepository = new InMemoryLedgerRepository();
        var service = new NodeWorkService(
            workRepository,
            membershipRepository,
            new LedgerService(ledgerRepository),
            dispatcher,
            new NodeSessionOptions
            {
                DispatchAckTimeoutSeconds = 5,
                DispatchExecutionTimeoutSeconds = 180,
                MaxPendingDispatchPerSession = 5
            });

        var created = await service.CreateAsync(new CreateNodeWorkRequest
        {
            WorkId = "work-push-1",
            RequestId = "req-push-1",
            TraceId = "trace-push-1",
            NodeId = "node-home-1",
            TenantId = "tenant-1",
            UserId = "user-1",
            TaskType = "ask.local",
            PolicyScope = "LOCAL_ONLY",
            WorkToken = "token-1",
            Payload = new Dictionary<string, object?> { ["question"] = "hello" }
        }, CancellationToken.None);

        created.IsSuccess.Should().BeTrue();
        dispatcher.Envelopes.Should().HaveCount(1);
        dispatcher.Envelopes[0].WorkId.Should().Be("work-push-1");
        dispatcher.Envelopes[0].RequestId.Should().Be("req-push-1");
        dispatcher.Envelopes[0].TraceContext.TraceId.Should().Be("trace-push-1");
        dispatcher.Envelopes[0].RequestKind.Should().Be("ask");
        dispatcher.Envelopes[0].TaskType.Should().Be("ask.local");
        dispatcher.Envelopes[0].PolicyScope.Should().Be("LOCAL_ONLY");
        dispatcher.Envelopes[0].LeaseId.Should().NotBeNullOrWhiteSpace();
        created.Data!.Status.Should().Be("leased");

        var ledgerEvents = await ledgerRepository.ListAsync(10, CancellationToken.None);
        ledgerEvents.Select(x => x.EventType).Should().Contain(new[]
        {
            "node_work.created",
            "node_work.dispatched"
        });
    }

    [Fact]
    public async Task Create_Lease_Acknowledge_And_Complete_Walks_Through_Mailbox_State()
    {
        var membershipRepository = new InMemoryMembershipRepository();
        await membershipRepository.AddAsync(new Member
        {
            NodeId = "node-home-1",
            TenantId = "tenant-1",
            OwnerUserId = "user-1",
            CertificateFingerprint = "fp-node-home-1",
            Status = MembershipStatus.Active
        }, CancellationToken.None);

        var workRepository = new InMemoryNodeWorkRepository();
        var ledgerRepository = new InMemoryLedgerRepository();
        var service = new NodeWorkService(workRepository, membershipRepository, new LedgerService(ledgerRepository));

        var created = await service.CreateAsync(new CreateNodeWorkRequest
        {
            WorkId = "work-1",
            NodeId = "node-home-1",
            TenantId = "tenant-1",
            UserId = "user-1",
            TaskType = "chat_history.import",
            IdempotencyKey = "idem-1",
            Payload = new Dictionary<string, object?> { ["file_name"] = "history.json" }
        }, CancellationToken.None);

        var leased = await service.LeaseAsync(new LeaseNodeWorkRequest
        {
            NodeId = "node-home-1",
            MaxItems = 1,
            LeaseDurationSeconds = 120
        }, CancellationToken.None);

        var leaseId = leased.Data!.Items.Single().LeaseId;
        var acknowledged = await service.AcknowledgeAsync("work-1", new AckNodeWorkRequest
        {
            NodeId = "node-home-1",
            LeaseId = leaseId!
        }, CancellationToken.None);

        var completed = await service.CompleteAsync("work-1", new CompleteNodeWorkRequest
        {
            NodeId = "node-home-1",
            LeaseId = leaseId!,
            Status = "completed",
            Result = new Dictionary<string, object?> { ["imported_chunks"] = 42 }
        }, CancellationToken.None);
        var fetched = await service.GetAsync("work-1", CancellationToken.None);

        created.IsSuccess.Should().BeTrue();
        leased.IsSuccess.Should().BeTrue();
        leased.Data!.Items.Should().HaveCount(1);
        leased.Data.Items.Single().Status.Should().Be("leased");
        acknowledged.IsSuccess.Should().BeTrue();
        acknowledged.Data!.Status.Should().Be("acknowledged");
        completed.IsSuccess.Should().BeTrue();
        completed.Data!.Status.Should().Be("completed");
        completed.Data.CompletedUtc.Should().NotBeNull();
        ((JsonElement)completed.Data.Result["imported_chunks"]!).GetInt32().Should().Be(42);
        fetched.IsSuccess.Should().BeTrue();
        fetched.Data!.Status.Should().Be("completed");

        var duplicate = await service.CreateAsync(new CreateNodeWorkRequest
        {
            NodeId = "node-home-1",
            TenantId = "tenant-1",
            TaskType = "chat_history.import",
            IdempotencyKey = "idem-1"
        }, CancellationToken.None);

        duplicate.IsSuccess.Should().BeTrue();
        duplicate.Data!.WorkId.Should().Be("work-1");

        var ledgerEvents = await ledgerRepository.ListAsync(10, CancellationToken.None);
        ledgerEvents.Select(x => x.EventType).Should().Contain(new[]
        {
            "node_work.created",
            "node_work.leased",
            "node_work.acknowledged",
            "node_work.completed"
        });
    }

    [Fact]
    public async Task AcknowledgeAsync_Extends_Lease_For_Local_Execution_Window()
    {
        var membershipRepository = new InMemoryMembershipRepository();
        await membershipRepository.AddAsync(new Member
        {
            NodeId = "node-home-1",
            TenantId = "tenant-1",
            OwnerUserId = "user-1",
            CertificateFingerprint = "fp-node-home-1",
            Status = MembershipStatus.Active
        }, CancellationToken.None);

        var dispatcher = new CapturingNodeSessionDispatcher();
        var workRepository = new InMemoryNodeWorkRepository();
        var ledgerRepository = new InMemoryLedgerRepository();
        var service = new NodeWorkService(
            workRepository,
            membershipRepository,
            new LedgerService(ledgerRepository),
            dispatcher,
            new NodeSessionOptions
            {
                DispatchAckTimeoutSeconds = 1,
                DispatchExecutionTimeoutSeconds = 120,
                MaxPendingDispatchPerSession = 5
            });

        var created = await service.CreateAsync(new CreateNodeWorkRequest
        {
            WorkId = "work-slow-1",
            RequestId = "req-slow-1",
            NodeId = "node-home-1",
            TenantId = "tenant-1",
            UserId = "user-1",
            TaskType = "ask.execute",
            WorkToken = "token-1",
            Payload = new Dictionary<string, object?> { ["question"] = "slow local model" }
        }, CancellationToken.None);

        var leaseId = created.Data!.LeaseId!;
        var dispatchLeaseExpiresUtc = created.Data.LeaseExpiresUtc;
        var acknowledged = await service.AcknowledgeAsync("work-slow-1", new AckNodeWorkRequest
        {
            NodeId = "node-home-1",
            LeaseId = leaseId
        }, CancellationToken.None);

        acknowledged.IsSuccess.Should().BeTrue();
        acknowledged.Data!.Status.Should().Be("acknowledged");
        acknowledged.Data.LeaseExpiresUtc.Should().NotBeNull();
        acknowledged.Data.LeaseExpiresUtc!.Value.Should().BeAfter(dispatchLeaseExpiresUtc!.Value.AddSeconds(30));
    }

    [Fact]
    public async Task CompleteAsync_Is_Idempotent_For_Duplicate_Terminal_Result()
    {
        var membershipRepository = new InMemoryMembershipRepository();
        await membershipRepository.AddAsync(new Member
        {
            NodeId = "node-home-1",
            TenantId = "tenant-1",
            OwnerUserId = "user-1",
            CertificateFingerprint = "fp-node-home-1",
            Status = MembershipStatus.Active
        }, CancellationToken.None);

        var workRepository = new InMemoryNodeWorkRepository();
        var ledgerRepository = new InMemoryLedgerRepository();
        var service = new NodeWorkService(workRepository, membershipRepository, new LedgerService(ledgerRepository));

        await service.CreateAsync(new CreateNodeWorkRequest
        {
            WorkId = "work-dupe-result-1",
            NodeId = "node-home-1",
            TenantId = "tenant-1",
            UserId = "user-1",
            TaskType = "ask.execute",
            Payload = new Dictionary<string, object?> { ["question"] = "hello" }
        }, CancellationToken.None);

        var leased = await service.LeaseAsync(new LeaseNodeWorkRequest
        {
            NodeId = "node-home-1",
            MaxItems = 1,
            LeaseDurationSeconds = 120
        }, CancellationToken.None);
        var leaseId = leased.Data!.Items.Single().LeaseId!;

        var completed = await service.CompleteAsync("work-dupe-result-1", new CompleteNodeWorkRequest
        {
            NodeId = "node-home-1",
            LeaseId = leaseId,
            Status = "completed",
            Result = new Dictionary<string, object?> { ["answer"] = "done" }
        }, CancellationToken.None);
        var duplicate = await service.CompleteAsync("work-dupe-result-1", new CompleteNodeWorkRequest
        {
            NodeId = "node-home-1",
            LeaseId = leaseId,
            Status = "completed",
            Result = new Dictionary<string, object?> { ["answer"] = "done" }
        }, CancellationToken.None);

        completed.IsSuccess.Should().BeTrue();
        duplicate.IsSuccess.Should().BeTrue();
        duplicate.Data!.Status.Should().Be("completed");
        duplicate.Data.CompletedUtc.Should().Be(completed.Data!.CompletedUtc);
    }

    private sealed class CapturingNodeSessionDispatcher : INodeSessionDispatcher
    {
        public List<NodeWorkEnvelopeMessage> Envelopes { get; } = [];

        public bool HasActiveSession(string nodeId)
        {
            return true;
        }

        public Task<bool> TryDispatchAsync(NodeWorkEnvelopeMessage envelope, CancellationToken cancellationToken)
        {
            Envelopes.Add(envelope);
            return Task.FromResult(true);
        }
    }
}
