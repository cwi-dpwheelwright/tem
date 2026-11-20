using Cwi.Common;
using Cwi.Orchestrator.Application.Model;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskFinalCompositionStepTests
{
    [Fact]
    public async Task ExecuteAsync_applies_communication_core_inline_without_model_roundtrip()
    {
        var modelClient = new ThrowingModelClient();
        var step = new AskFinalCompositionStep(modelClient, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "What is CWI?", "pack", null, "observe", 3)
        {
            Answer = "Styled final answer.\r\n\r\n\r\n",
            QueryClassification = "identity_definition",
            AudienceMode = "home_user",
            VerbosityMode = "detailed",
            ModelName = "model-1",
            BaselinePackGuidance = new[]
            {
                new BaselinePackGuidance(
                    "cwi-communication-core",
                    "1.0.0",
                    "hash-1",
                    "Be clear and direct.",
                    moduleRole: "global_communication_layer",
                    applyStage: "final_response_composition",
                    hookName: "apply_communication_core")
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Be("Styled final answer.");
        context.CommunicationCoreApplied.Should().BeTrue();
    }

    private sealed class ThrowingModelClient : IModelClient
    {
        public Task<ServiceResponse<ModelResult>> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            throw new Xunit.Sdk.XunitException("Final composition should not invoke the model client.");
        }
    }
}
