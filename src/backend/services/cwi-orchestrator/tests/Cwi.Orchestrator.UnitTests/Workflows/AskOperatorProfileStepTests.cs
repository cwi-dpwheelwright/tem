using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.OperatorMemory;
using Cwi.Orchestrator.Application.Telemetry;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskOperatorProfileStepTests
{
    [Fact]
    public async Task ExecuteAsync_records_profile_decision_metrics()
    {
        var options = new OperatorMemoryOptions
        {
            CorrelationScoreThreshold = 0.8
        };
        var repository = new InMemoryOperatorProfileRepository();
        await repository.UpsertAsync(new OperatorProfile
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            OperatorId = "operator-1",
            SignalsTotal = 10,
            CorrectionsTotal = 5,
            DelaysTotal = 0,
            ConfirmationsTotal = 5,
            LatencySum = 0,
            LatencyCount = 0,
            CorrelationScore = 0.3,
            LastUpdatedUtc = DateTimeOffset.UtcNow
        }, CancellationToken.None);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var profileService = new OperatorProfileService(repository, cache, options);
        var policyService = new OperatorProfilePolicyService(options);
        var metrics = new OrchestratorMetrics();
        var step = new AskOperatorProfileStep(profileService, policyService, metrics);
        var context = new AskWorkflowContext(
            tenantId: "tenant-1",
            userId: "operator-1",
            question: "What is the status?",
            packName: "default",
            requestedPackAnchor: null,
            policyMode: "standard",
            topK: 3);

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.PolicyMode.Should().Be("conservative");
        context.OperatorProfileDecision.Should().NotBeNull();
        context.OperatorProfileDecision!.Applied.Should().BeTrue();

        var snapshot = metrics.Snapshot();
        snapshot.OperatorProfileEvaluationsByRule.Should().ContainKey("profile:low_correlation");
        snapshot.OperatorProfileAdjustmentsByRule.Should().ContainKey("profile:low_correlation");
        snapshot.OperatorProfileAdjustmentsByRule["profile:low_correlation"].Should().Be(1);
    }

    private sealed class InMemoryOperatorProfileRepository : IOperatorProfileRepository
    {
        private readonly Dictionary<string, OperatorProfile> _profiles = new();

        public Task<OperatorProfile?> GetAsync(string tenantId, string operatorId, CancellationToken cancellationToken)
        {
            _profiles.TryGetValue(Key(tenantId, operatorId), out var profile);
            return Task.FromResult(profile);
        }

        public Task UpsertAsync(OperatorProfile profile, CancellationToken cancellationToken)
        {
            _profiles[Key(profile.TenantId, profile.OperatorId)] = profile;
            return Task.CompletedTask;
        }

        private static string Key(string tenantId, string operatorId)
        {
            return $"{tenantId.Trim().ToLowerInvariant()}::{operatorId.Trim().ToLowerInvariant()}";
        }
    }
}
