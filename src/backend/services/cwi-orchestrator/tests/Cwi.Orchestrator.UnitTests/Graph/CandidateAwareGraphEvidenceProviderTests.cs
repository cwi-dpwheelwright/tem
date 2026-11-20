#nullable enable
using Cwi.Common;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Graph;

public sealed class CandidateAwareGraphEvidenceProviderTests
{
    [Fact]
    public async Task RetrieveAsync_adds_matching_current_graph_candidates_as_evidence()
    {
        var repository = new FakeGraphUpdateCandidateRepository();
        repository.Candidates.Add(new GraphUpdateCandidateRecord
        {
            CandidateId = "candidate-1",
            TenantId = "tenant-1",
            CandidateState = "add",
            TemporalState = "current",
            Subject = "dashboard telemetry contract",
            Relation = "owned_by",
            Object = "PlatformOps",
            FactKey = "dashboard.telemetry.owner",
            RecursiveEventId = "event-1",
            TraceId = "trace-1",
            SourceComponent = "structural_validation_runner",
            SourceHash = "hash-1",
            PayloadJson = "{}"
        });
        var provider = new CandidateAwareGraphEvidenceProvider(new EmptyGraphEvidenceProvider(), repository);

        var result = await provider.RetrieveAsync(
            new GraphEvidenceRequest(
                "Using governed graph evidence, which role owns dashboard telemetry contract?",
                "tenant-1",
                TopK: 5),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Template.Should().Be("graph_update_candidate");
        result.Data.Evidence.Should().ContainSingle(evidence =>
            evidence.SourceDocument == "candidate-1"
            && evidence.Subject == "dashboard telemetry contract"
            && evidence.Object == "PlatformOps");
    }

    private sealed class EmptyGraphEvidenceProvider : IGraphEvidenceProvider
    {
        public Task<ServiceResponse<GraphQueryResult>> RetrieveAsync(
            GraphEvidenceRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(ServiceResponse<GraphQueryResult>.Success(new GraphQueryResult()));
        }
    }

    private sealed class FakeGraphUpdateCandidateRepository : IGraphUpdateCandidateRepository
    {
        public List<GraphUpdateCandidateRecord> Candidates { get; } = new();

        public Task<bool> ExistsForRecursiveEventAsync(
            string recursiveEventId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Candidates.Any(candidate => candidate.RecursiveEventId == recursiveEventId));
        }

        public Task AddAsync(
            GraphUpdateCandidateRecord record,
            CancellationToken cancellationToken)
        {
            Candidates.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GraphUpdateCandidateRecord>> QueryAsync(
            string? tenantId,
            string? candidateState,
            string? temporalState,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            IEnumerable<GraphUpdateCandidateRecord> query = Candidates;
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                query = query.Where(candidate => candidate.TenantId == tenantId);
            }

            if (!string.IsNullOrWhiteSpace(candidateState))
            {
                query = query.Where(candidate => candidate.CandidateState == candidateState);
            }

            if (!string.IsNullOrWhiteSpace(temporalState))
            {
                query = query.Where(candidate => candidate.TemporalState == temporalState);
            }

            return Task.FromResult<IReadOnlyList<GraphUpdateCandidateRecord>>(
                query.Skip(skip).Take(take).ToList());
        }

        public Task<GraphUpdateCandidateRecord?> GetByCandidateIdAsync(
            string candidateId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Candidates.SingleOrDefault(candidate => candidate.CandidateId == candidateId));
        }
    }
}
#nullable disable
