using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Orchestrator.Application;
using Cwi.Orchestrator.Application.Action;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Action;

public sealed class ActionServiceTests
{

    [Fact]
    public async Task HandleAsync_allows_missing_pack_version()
    {
        var step = new RecordingStep();
        var pipeline = new ActionWorkflowPipeline(new[] { step }, new IActionWorkflowDecorator[0]);
        var options = new ApplicationOptions(
            packsBaseUrl: "http://packs",
            packsDirectory: null,
            graphEnabled: false,
            graphDbConnectionString: null,
            graphName: "cwi_graph",
            graphQueryTimeoutSeconds: 5,
            graphTopK: 5,
            vectorShard1Url: "http://v1",
            vectorShard2Url: "http://v2",
            vectorEmbeddingDimension: 8,
            modelBaseUrl: "http://model",
            modelName: "model-default",
            modelTimeoutSeconds: 5,
            policyVersion: "v1",
            ledgerHashSuiteId: "sha256-v1");

        var service = new ActionService(pipeline, options);
        var request = new ActionRequestDto
        {
            TenantId = "tenant",
            UserId = "user",
            PolicyMode = "approve",
            Action = new ActionDescriptorDto
            {
                Type = "risk_demo",
                Payload = new Dictionary<string, object?> { ["value"] = "test" }
            },
            PackHash = "hash-1",
            PackVersion = "",
            ModelId = "model-1",
            PolicyVersion = "v1"
        };

        var response = await service.HandleAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        step.Executed.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_allows_missing_pack_hash()
    {
        var step = new RecordingStep();
        var pipeline = new ActionWorkflowPipeline(new[] { step }, new IActionWorkflowDecorator[0]);
        var options = new ApplicationOptions(
            packsBaseUrl: "http://packs",
            packsDirectory: null,
            graphEnabled: false,
            graphDbConnectionString: null,
            graphName: "cwi_graph",
            graphQueryTimeoutSeconds: 5,
            graphTopK: 5,
            vectorShard1Url: "http://v1",
            vectorShard2Url: "http://v2",
            vectorEmbeddingDimension: 8,
            modelBaseUrl: "http://model",
            modelName: "model-default",
            modelTimeoutSeconds: 5,
            policyVersion: "v1",
            ledgerHashSuiteId: "sha256-v1");

        var service = new ActionService(pipeline, options);
        var request = new ActionRequestDto
        {
            TenantId = "tenant",
            UserId = "user",
            PolicyMode = "approve",
            Action = new ActionDescriptorDto
            {
                Type = "risk_demo",
                Payload = new Dictionary<string, object?> { ["value"] = "test" }
            },
            PackHash = "",
            PackVersion = "1.0.0",
            ModelId = "model-1",
            PolicyVersion = "v1"
        };

        var response = await service.HandleAsync(request, CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        step.Executed.Should().BeTrue();
    }

    private sealed class RecordingStep : IActionWorkflowStep
    {
        public bool Executed { get; private set; }

        public Task<ServiceResponse<ActionResponseDto>> ExecuteAsync(
            ActionWorkflowContext context,
            WorkflowStepDelegate<ActionResponseDto> next,
            CancellationToken cancellationToken)
        {
            Executed = true;
            return Task.FromResult(ServiceResponse<ActionResponseDto>.Success(new ActionResponseDto()));
        }
    }
}
