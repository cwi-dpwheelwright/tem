using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Application.Packs.Policy;
using Cwi.Orchestrator.Application.Policy;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskPackCapabilityStepTests
{
    [Fact]
    public async Task ExecuteAsync_creates_approval_ticket_when_required()
    {
        var evaluator = new PackCapabilityPolicyEvaluator();
        var repo = new FakeApprovalTicketRepository();
        var step = new AskPackCapabilityStep(evaluator, repo);

        var capabilities = new List<PackCapability>
        {
            new PackCapability("shutdown", "action", RiskLevel.High, requiresApproval: true, description: null)
        };

        var context = new AskWorkflowContext("tenant", "user", "question", "pack", null, "approve", 3)
        {
            PackHash = "pack-hash",
            PackCapabilities = new PackCapabilityMap(capabilities)
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeFalse();
        response.StatusCode.Should().Be(403);
        response.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        response.Error!.Message.Should().Contain("Ticket:");

        repo.Stored.Should().NotBeNull();
        repo.Stored!.TenantId.Should().Be("tenant");
        context.TraceId.Should().NotBeNullOrWhiteSpace();
    }

    private sealed class FakeApprovalTicketRepository : IApprovalTicketRepository
    {
        public ApprovalTicket? Stored { get; private set; }

        public Task<ApprovalTicket?> GetByTicketIdAsync(string ticketId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Stored?.TicketId == ticketId ? Stored : null);
        }

        public Task<ApprovalTicket?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(Stored?.IdempotencyKey == idempotencyKey ? Stored : null);
        }

        public Task<IReadOnlyList<ApprovalTicket>> ListByTenantIdAsync(string tenantId, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ApprovalTicket>>(new List<ApprovalTicket>());
        }

        public Task AddAsync(ApprovalTicket ticket, CancellationToken cancellationToken)
        {
            Stored = ticket;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ApprovalTicket ticket, CancellationToken cancellationToken)
        {
            Stored = ticket;
            return Task.CompletedTask;
        }
    }
}
