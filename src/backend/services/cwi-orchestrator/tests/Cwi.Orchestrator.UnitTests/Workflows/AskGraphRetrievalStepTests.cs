using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskGraphRetrievalStepTests
{
    [Fact]
    public async Task ExecuteAsync_records_graph_evidence_when_provider_succeeds()
    {
        var provider = new FakeGraphEvidenceProvider(
            ServiceResponse<GraphQueryResult>.Success(new GraphQueryResult
            {
                Template = "equipment_metrics",
                Terms = new[] { "micromat" },
                Evidence = new[]
                {
                    new GraphEvidence
                    {
                        Subject = "Micromat",
                        Relation = "MEASURED_BY",
                        Object = "amperage",
                        Template = "equipment_metrics"
                    }
                }
            }));
        var step = new AskGraphRetrievalStep(provider, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "question", "pack", null, "observe", 3)
        {
            RetrievalDomain = "equipment_metrics",
            RetrievalDomainTerms = new[] { "micromat" }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.GraphEvidence.Should().ContainSingle();
        context.GraphQueryTemplate.Should().Be("equipment_metrics");
        context.GraphUnavailable.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_marks_graph_unavailable_when_provider_fails()
    {
        var provider = new FakeGraphEvidenceProvider(
            ServiceResponse<GraphQueryResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "Graph query failed."),
                statusCode: 503,
                message: "Graph query failed"));
        var step = new AskGraphRetrievalStep(provider, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "question", "pack", null, "observe", 3)
        {
            RetrievalDomain = "equipment_metrics",
            RetrievalDomainTerms = new[] { "micromat" }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.GraphUnavailable.Should().BeTrue();
        context.GraphFailureReason.Should().Be("Graph query failed");
        context.GraphEvidence.Should().BeEmpty();
    }

    private sealed class FakeGraphEvidenceProvider : IGraphEvidenceProvider
    {
        private readonly ServiceResponse<GraphQueryResult> _result;

        public FakeGraphEvidenceProvider(ServiceResponse<GraphQueryResult> result)
        {
            _result = result;
        }

        public Task<ServiceResponse<GraphQueryResult>> RetrieveAsync(GraphEvidenceRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_result);
        }
    }
}
