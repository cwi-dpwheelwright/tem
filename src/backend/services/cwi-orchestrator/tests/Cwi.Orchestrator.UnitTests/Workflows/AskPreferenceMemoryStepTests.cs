using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.OperatorMemory;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskPreferenceMemoryStepTests
{
    [Fact]
    public async Task ExecuteAsync_stores_preference_and_marks_response_ready()
    {
        var memory = new OperatorPreferenceMemory();
        var step = new AskPreferenceMemoryStep(memory, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "My preferred output is step-by-step instructions.", "pack", null, "observe", 3)
        {
            QueryClassification = "preference_update",
            MemoryUpdateKind = "output_style",
            MemoryUpdateValue = "step_by_step"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.MemoryUpdated.Should().BeTrue();
        context.SkipModelInference.Should().BeTrue();
        context.RetrievalAllowed.Should().BeFalse();
        context.GroundingMode.Should().Be("memory_update");
        context.Answer.Should().Contain("Preference saved");
        memory.Get("tenant", "user", "output_style")!.Value.Should().Be("step_by_step");
    }

    [Fact]
    public async Task ExecuteAsync_applies_stored_preference_to_future_requests()
    {
        var memory = new OperatorPreferenceMemory();
        memory.Update("tenant", "user", "output_style", "step_by_step", "My preferred output is step-by-step instructions.");
        var step = new AskPreferenceMemoryStep(memory, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "Explain the deploy check.", "pack", null, "observe", 3);

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.VerbosityMode.Should().Be("step_by_step");
        context.AppliedPreferenceMode.Should().Be("step_by_step");
    }

    [Fact]
    public async Task ExecuteAsync_does_not_let_memory_override_current_decision_intent()
    {
        var memory = new OperatorPreferenceMemory();
        memory.Update("tenant", "user", "output_style", "step_by_step", "My preferred output is step-by-step instructions.");
        var step = new AskPreferenceMemoryStep(memory, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "Give me an executive summary for the investor demo.", "pack", null, "observe", 3)
        {
            QueryClassification = "general_reasoning",
            IntentType = "decision_support",
            AudienceMode = "executive"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.VerbosityMode.Should().BeNull();
        context.AppliedPreferenceMode.Should().BeNull();
    }
}
