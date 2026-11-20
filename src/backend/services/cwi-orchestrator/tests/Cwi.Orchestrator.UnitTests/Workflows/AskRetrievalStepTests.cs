using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Retrieval;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskRetrievalStepTests
{
    [Fact]
    public async Task ExecuteAsync_skips_vector_provider_when_retrieval_policy_denies()
    {
        var provider = new FakeRetrievalProvider();
        var step = new AskRetrievalStep(provider, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "Give me a deployment plan.", "pack", null, "observe", 3)
        {
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            RetrievalSkippedReason = "missing_retrieval_intent_or_domain_context"
        };
        context.Retrieved.Add(new RetrievalResult("old-doc", "old-chunk", "old content", 0.99f));

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        provider.Calls.Should().Be(0);
        context.Retrieved.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_filters_results_by_score_and_domain_terms()
    {
        var provider = new FakeRetrievalProvider
        {
            Results = new[]
            {
                new RetrievalResult("graph-tooling", "chunk-1", "Graph tooling implementation plan.", 0.95f),
                new RetrievalResult("docker-runbook", "chunk-1", "Docker container curl health checks.", 0.88f),
                new RetrievalResult("docker-low-score", "chunk-1", "Docker note.", 0.2f)
            }
        };
        var step = new AskRetrievalStep(provider, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "My Docker container restarted but curl failed.", "pack", null, "observe", 3)
        {
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "container_operations",
            RetrievalDomainTerms = new[] { "docker", "container", "curl" },
            RetrievalMinScore = 0.35f
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        provider.Calls.Should().Be(1);
        context.Retrieved.Should().ContainSingle();
        context.Retrieved[0].DocId.Should().Be("docker-runbook");
    }

    private sealed class FakeRetrievalProvider : IRetrievalProvider
    {
        public int Calls { get; private set; }
        public IReadOnlyList<RetrievalResult> Results { get; init; } = Array.Empty<RetrievalResult>();

        public Task<ServiceResponse<IReadOnlyList<RetrievalResult>>> RetrieveAsync(
            string collection,
            string query,
            int topK,
            RetrievalFilter? filter,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(ServiceResponse<IReadOnlyList<RetrievalResult>>.Success(Results));
        }
    }
}
