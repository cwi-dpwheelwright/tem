using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Policy;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class ActionDecisionStepTests
{
    [Fact]
    public async Task ExecuteAsync_escalates_high_discrepancy_for_high_risk_action()
    {
        var evaluator = new PolicyEvaluator();
        var tickets = new InMemoryApprovalTicketRepository();
        var step = new ActionDecisionStep(evaluator, tickets);

        var payload = new Dictionary<string, object?>
        {
            ["discrepancy_severity"] = "high"
        };
        var context = new ActionWorkflowContext("tenant", "user", "observe", "transfer_funds", payload)
        {
            ModelName = "model",
            PolicyVersion = "v1",
            PackHash = "hash"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<ActionResponseDto>.Success(new ActionResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.ApprovalTicket.Should().NotBeNull();
        context.Executed.Should().BeFalse();
    }

    private sealed class InMemoryApprovalTicketRepository : IApprovalTicketRepository
    {
        private readonly List<ApprovalTicket> _tickets = new();

        public Task<ApprovalTicket?> GetByTicketIdAsync(string ticketId, CancellationToken cancellationToken)
        {
            return Task.FromResult(_tickets.Find(x => x.TicketId == ticketId));
        }

        public Task<ApprovalTicket?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(_tickets.Find(x => x.IdempotencyKey == idempotencyKey));
        }

        public Task<IReadOnlyList<ApprovalTicket>> ListByTenantIdAsync(string tenantId, int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<ApprovalTicket>>(_tickets);
        }

        public Task AddAsync(ApprovalTicket ticket, CancellationToken cancellationToken)
        {
            _tickets.Add(ticket);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ApprovalTicket ticket, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
