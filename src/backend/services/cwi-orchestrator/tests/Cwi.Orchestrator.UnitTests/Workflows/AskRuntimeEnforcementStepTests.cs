using Cwi.Common;
using Cwi.Common.Errors;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Model;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskRuntimeEnforcementStepTests
{
    [Fact]
    public async Task Communication_runtime_blocks_troubleshooting_retrieval_before_composition()
    {
        var step = new AskCommunicationRuntimeStep(JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "My Docker container restarted but curl failed. What should I check next?",
            "pack",
            null,
            "observe",
            3)
        {
            QueryClassification = "troubleshooting",
            IntentType = "troubleshooting",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomainTerms = new[] { "docker", "container", "curl" }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.CommunicationRuntimeApplied.Should().BeTrue();
        context.TemplateUsed.Should().Be("operator_troubleshooting");
        context.ToneMode.Should().Be("direct_step_by_step");
        context.VerbosityMode.Should().Be("step_by_step");
        context.RetrievalAllowed.Should().BeFalse();
        context.GroundingMode.Should().Be("non_grounded");
        context.RetrievalSkippedReason.Should().Be("troubleshooting_without_explicit_source_request");
    }

    [Fact]
    public async Task Communication_runtime_maps_executive_intent_to_tone_before_template()
    {
        var step = new AskCommunicationRuntimeStep(JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "Give me an executive summary for the investor demo.",
            "pack",
            null,
            "observe",
            3)
        {
            QueryClassification = "general_reasoning",
            IntentType = "decision_support",
            AudienceMode = "executive",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.ToneMode.Should().Be("executive_concise");
        context.TemplateUsed.Should().Be("executive_summary");
        context.VerbosityMode.Should().Be("concise");
    }

    [Fact]
    public async Task Communication_runtime_maps_risk_intent_to_risk_template()
    {
        var step = new AskCommunicationRuntimeStep(JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "what's at risk right now",
            "enterprise",
            null,
            "observe",
            3)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "risk_review",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            EvidenceRequired = true
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.TemplateUsed.Should().Be("risk_summary");
        context.ToneMode.Should().Be("risk_concise");
        context.VerbosityMode.Should().Be("concise");
    }

    [Fact]
    public async Task Communication_runtime_preserves_risk_template_when_evidence_route_is_blocked()
    {
        var step = new AskCommunicationRuntimeStep(JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "what's at risk right now",
            "enterprise",
            null,
            "observe",
            3)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "risk_review",
            RetrievalAllowed = false,
            GroundingMode = "source_grounded",
            EvidenceRequired = true
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.TemplateUsed.Should().Be("risk_summary");
        context.ToneMode.Should().Be("risk_concise");
        context.RetrievalSkippedReason.Should().Be("evidence_required_but_missing_domain_context");
    }

    [Fact]
    public async Task Communication_runtime_maps_daily_briefing_intent_to_briefing_template()
    {
        var step = new AskCommunicationRuntimeStep(JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "daily briefing",
            "enterprise",
            null,
            "observe",
            3)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "daily_briefing",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            EvidenceRequired = true
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.TemplateUsed.Should().Be("daily_briefing");
        context.ToneMode.Should().Be("briefing_concise");
        context.VerbosityMode.Should().Be("concise");
    }

    [Fact]
    public async Task Communication_runtime_records_comprehension_expression_decisions()
    {
        var step = new AskCommunicationRuntimeStep(JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "Explain this API key leak casually.",
            "pack",
            null,
            "observe",
            3)
        {
            QueryClassification = "general_reasoning",
            IntentType = "general_model_knowledge",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.ComprehensionExpressionVersion.Should().Be(ComprehensionExpressionFrameworkService.Version);
        context.ContextAwarenessMode.Should().Be("sensitive");
        context.ClarityDecision.Should().Be("clarity_no_change_needed");
        context.ExpressionToneProfile.Should().Be("tone_sensitive");
        context.ExpressionToneReason.Should().Be("tone_safety_override");
        context.ToneMode.Should().Be("tone_sensitive");
    }

    [Fact]
    public async Task Communication_runtime_prioritizes_fail_closed_over_troubleshooting_template()
    {
        var step = new AskCommunicationRuntimeStep(JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "According to our docs, why is the unknown subsystem failing?",
            "pack",
            null,
            "observe",
            3)
        {
            QueryClassification = "troubleshooting",
            IntentType = "troubleshooting",
            EvidenceRequired = true,
            RetrievalAllowed = false,
            GroundingMode = "source_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.ToneMode.Should().Be("careful_escalation");
        context.TemplateUsed.Should().Be("low_confidence_escalation");
    }

    [Fact]
    public async Task Evidence_requirement_step_returns_usable_blocked_response_when_sources_are_missing()
    {
        var step = new AskEvidenceRequirementStep(JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "According to our docs, what does the unknown foobar subsystem do?",
            "pack",
            null,
            "observe",
            3)
        {
            QueryClassification = "general_reasoning",
            EvidenceRequired = true,
            RetrievalAllowed = false,
            GroundingMode = "source_grounded",
            RetrievalSkippedReason = "missing_retrieval_domain_context",
            TemplateUsed = "low_confidence_escalation"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("do not have enough governed evidence");
        context.SkipModelInference.Should().BeTrue();
        context.FallbackApplied.Should().BeTrue();
        context.TemplateUsed.Should().Be("low_confidence_escalation");
        context.ToneMode.Should().Be("careful_escalation");
        context.ResponseWarnings.Should().Contain("evidence_required_but_no_governed_evidence");
    }

    [Fact]
    public async Task Evidence_requirement_step_does_not_treat_conversation_memory_as_required_source_evidence()
    {
        var step = new AskEvidenceRequirementStep(JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "According to our docs, what does the unknown foobar subsystem do?",
            "pack",
            null,
            "observe",
            3)
        {
            QueryClassification = "general_reasoning",
            EvidenceRequired = true,
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomainTerms = new[] { "foobar", "subsystem" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "hist-prior-failure",
                    MemoryScope = "conversation",
                    SourceType = "conversation_turn",
                    Text = "Prior turn: CWI answered that there was not enough governed evidence.",
                    TenantId = "tenant",
                    UserId = "user",
                    RelevanceScore = 0.99f,
                    Confidence = 0.90f
                }
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("do not have enough governed evidence");
        context.SkipModelInference.Should().BeTrue();
        context.TemplateUsed.Should().Be("low_confidence_escalation");
        context.ToneMode.Should().Be("careful_escalation");
        context.MemoryResults.Should().BeEmpty();
    }

    [Fact]
    public async Task Inference_step_uses_fallback_instead_of_failing_empty_runtime_paths()
    {
        var step = new AskInferenceStep(new FailingModelClient(), JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "My Docker container restarted but curl failed. What should I check next?",
            "pack",
            null,
            "observe",
            3)
        {
            Prompt = "prompt",
            IntentType = "troubleshooting",
            TemplateUsed = "operator_troubleshooting"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Start here");
        context.Answer.Should().Contain("Verification command");
        context.FallbackApplied.Should().BeTrue();
        context.ModelName.Should().Be("governed-fallback-response");
        context.ResponseWarnings.Should().Contain("model_inference_unavailable");
    }

    private sealed class FailingModelClient : IModelClient
    {
        public Task<ServiceResponse<ModelResult>> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ServiceResponse<ModelResult>.Fail(
                new DomainError(ErrorCodes.DependencyUnavailable, "offline"),
                statusCode: 503,
                message: "offline"));
        }
    }
}
