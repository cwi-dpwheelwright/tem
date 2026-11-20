using Cwi.Common;
using Cwi.Orchestrator.Application.Model;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskResponseOrchestrationStepTests
{
    [Fact]
    public async Task ExecuteAsync_applies_response_orchestrator_inline_without_model_roundtrip()
    {
        var modelClient = new ThrowingModelClient();
        var step = new AskResponseOrchestrationStep(modelClient, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant", "user", "Explain CWI to a home user.", "pack", null, "observe", 3)
        {
            Answer = "Draft answer.\r\n\r\n\r\nThen explanation.   ",
            QueryClassification = "identity_definition",
            IntentType = "explanation",
            AudienceMode = "home_user",
            VerbosityMode = "detailed",
            ModelName = "model-1",
            BaselinePackGuidance = new[]
            {
                new BaselinePackGuidance(
                    "cwi-response-orchestrator",
                    "1.0.0",
                    "hash-1",
                    "Direct answer, then explanation."),
                new BaselinePackGuidance(
                    "cwi-safety-guardrails",
                    "1.0.0",
                    "hash-2",
                    "Do not fabricate information."),
                new BaselinePackGuidance(
                    "cwi-identity",
                    "1.0.0",
                    "hash-3",
                    "CWI stands for Christian Wheelwright Intelligence.",
                    moduleRole: "system_truth")
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Be("Draft answer.\n\nThen explanation.");
    }

    [Fact]
    public async Task ExecuteAsync_enforces_operator_troubleshooting_template()
    {
        var modelClient = new ThrowingModelClient();
        var step = new AskResponseOrchestrationStep(modelClient, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "My Docker container restarted but curl failed. What should I check next?",
            "pack",
            null,
            "observe",
            3)
        {
            Answer = "Check the container status and logs.",
            IntentType = "troubleshooting",
            TemplateUsed = "operator_troubleshooting",
            RetrievalDomainTerms = new[] { "docker", "container", "curl" },
            BaselinePackGuidance = new[]
            {
                new BaselinePackGuidance(
                    "cwi-response-orchestrator",
                    "1.0.0",
                    "hash-1",
                    "Use the selected response template.")
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Start here:");
        context.Answer.Should().Contain("1. Check the container status and logs.");
        context.Answer.Should().Contain("Verification command:");
        context.Answer.Should().Contain("docker ps");
        context.Answer.Should().Contain("curl -v");
    }

    [Fact]
    public async Task ExecuteAsync_enforces_selected_template_without_response_orchestrator_pack()
    {
        var modelClient = new ThrowingModelClient();
        var step = new AskResponseOrchestrationStep(modelClient, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "Give me a deployment plan.",
            "pack",
            null,
            "observe",
            3)
        {
            Answer = "Validate, deploy, and monitor.",
            IntentType = "operational",
            TemplateUsed = "operator_steps"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Steps:");
        context.Answer.Should().Contain("1. Validate, deploy, and monitor.");
        context.PresentationFrameworkVersion.Should().Be(PresentationFrameworkService.Version);
        context.PresentationTemplateFamily.Should().Be("default_helpful");
        context.PresentationRhythmMode.Should().Be("rhythm_compact");
        context.PresentationFormattingMode.Should().Be("format_default");
    }

    [Fact]
    public async Task ExecuteAsync_records_presentation_decision_for_response_mode_templates()
    {
        var modelClient = new ThrowingModelClient();
        var step = new AskResponseOrchestrationStep(modelClient, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "what's at risk right now",
            "enterprise",
            null,
            "observe",
            3)
        {
            Answer = "The enterprise policy framework says to evaluate risk posture before proceeding.",
            IntentType = "risk_review",
            TemplateUsed = "risk_summary",
            BaselinePackGuidance = new[]
            {
                new BaselinePackGuidance(
                    "cwi-response-orchestrator",
                    "1.1.0",
                    "hash-1",
                    "Enforce risk response behavior.")
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.PresentationFrameworkVersion.Should().Be(PresentationFrameworkService.Version);
        context.PresentationTemplateFamily.Should().Be("risk");
        context.PresentationTemplateReason.Should().Be("template_risk");
        context.PresentationRhythmMode.Should().Be("rhythm_structured");
        context.PresentationFormattingMode.Should().Be("format_structured_sections");
        context.PresentationSourceDisplayMode.Should().Be("source_display_none");
    }

    [Fact]
    public async Task ExecuteAsync_keeps_low_confidence_template_concise_and_hides_internal_scaffolding()
    {
        var modelClient = new ThrowingModelClient();
        var step = new AskResponseOrchestrationStep(modelClient, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "According to our docs, what does the unknown subsystem do?",
            "pack",
            null,
            "observe",
            3)
        {
            Answer = """
                Query class: general_reasoning
                Intent type: troubleshooting
                Response template: low_confidence_escalation
                I do not have enough governed evidence to answer that safely.
                """,
            EvidenceRequired = true,
            TemplateUsed = "low_confidence_escalation",
            RetrievalAllowed = false,
            GroundingMode = "source_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Be("I do not have enough governed evidence to answer that safely. Please name the source domain, runbook, repo area, log, or CWI component you want me to use, and I can retry with governed retrieval.");
        context.Answer.Should().NotContain("Query class:");
        context.Answer.Should().NotContain("Response template:");
    }

    [Fact]
    public async Task ExecuteAsync_removes_internal_analysis_lines_from_user_answer()
    {
        var modelClient = new ThrowingModelClient();
        var step = new AskResponseOrchestrationStep(modelClient, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "Summarize the impact of this system.",
            "pack",
            null,
            "observe",
            3)
        {
            Answer = """
                Intent identification: decision support.
                Evidence review: no governed retrieval requested.
                Template analysis: executive_summary.
                Impact: The system reduces operator confusion.

                Recommendation: Keep the demo path focused.
                """,
            IntentType = "decision_support",
            TemplateUsed = "executive_summary"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().StartWith("Impact:");
        context.Answer.Should().Contain("Recommendation:");
        context.Answer.Should().NotContain("Intent identification");
        context.Answer.Should().NotContain("Evidence review");
        context.Answer.Should().NotContain("Template analysis");
    }

    [Fact]
    public async Task ExecuteAsync_does_not_prefix_self_context_updates_as_preferences()
    {
        var modelClient = new ThrowingModelClient();
        var step = new AskResponseOrchestrationStep(modelClient, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "My name is Demitrius Wheelwright and I am a Software Engineer",
            "pack",
            null,
            "observe",
            3)
        {
            Answer = "Got it, Demitrius Wheelwright. I will use Software Engineer as your work context.",
            QueryClassification = "self_context_update",
            IntentType = "memory_update",
            TemplateUsed = "memory_acknowledgement",
            GroundingMode = "memory_update",
            RetrievalAllowed = false
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Be("Got it, Demitrius Wheelwright. I will use Software Engineer as your work context.");
        context.Answer.Should().NotStartWith("Preference saved.");
        context.Answer.Should().NotContain("User stated durable context");
        context.Answer.Should().NotContain("available context");
    }

    [Fact]
    public async Task ExecuteAsync_enforces_final_response_behavior_limits()
    {
        var modelClient = new ThrowingModelClient();
        var step = new AskResponseOrchestrationStep(modelClient, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "Summarize the impact of this system.",
            "pack",
            null,
            "observe",
            3)
        {
            Answer = """
                Priorities:
                1. First priority.
                2. Second priority.
                3. Third priority.
                4. Fourth priority.

                Risks:
                1. First risk.
                2. Second risk.

                Next steps:
                1. First next step.
                2. Second next step.

                This should not use enterprise policy/framework language.
                """,
            IntentType = "decision_support",
            TemplateUsed = "executive_summary",
            BaselinePackGuidance = new[]
            {
                new BaselinePackGuidance(
                    "cwi-response-orchestrator",
                    "1.1.0",
                    "hash-1",
                    "Enforce concise response behavior.")
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("1. First priority.");
        context.Answer.Should().Contain("3. Third priority.");
        context.Answer.Should().NotContain("4. Fourth priority.");
        context.Answer.Should().Contain("1. First risk.");
        context.Answer.Should().NotContain("2. Second risk.");
        context.Answer.Should().Contain("1. First next step.");
        context.Answer.Should().NotContain("2. Second next step.");
        context.Answer.Should().NotContain("policy/framework");
    }

    [Fact]
    public async Task ExecuteAsync_enforces_risk_response_mode()
    {
        var modelClient = new ThrowingModelClient();
        var step = new AskResponseOrchestrationStep(modelClient, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "what's at risk right now",
            "enterprise",
            null,
            "observe",
            3)
        {
            Answer = "The enterprise policy framework says to evaluate risk posture before proceeding.",
            IntentType = "risk_review",
            TemplateUsed = "risk_summary",
            BaselinePackGuidance = new[]
            {
                new BaselinePackGuidance(
                    "cwi-response-orchestrator",
                    "1.1.0",
                    "hash-1",
                    "Enforce risk response behavior.")
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Risks:");
        context.Answer.Should().Contain("Impact:");
        context.Answer.Should().Contain("Next action:");
        context.Answer.Should().NotContain("policy framework");
    }

    [Fact]
    public async Task ExecuteAsync_allows_two_risks_for_risk_response_mode()
    {
        var modelClient = new ThrowingModelClient();
        var step = new AskResponseOrchestrationStep(modelClient, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "what's at risk right now",
            "enterprise",
            null,
            "observe",
            3)
        {
            Answer = """
                Risks:
                1. First risk.
                2. Second risk.
                3. Third risk.

                Impact:
                These risks can affect delivery.

                Next action:
                Confirm ownership today.
                """,
            IntentType = "risk_review",
            TemplateUsed = "risk_summary",
            BaselinePackGuidance = new[]
            {
                new BaselinePackGuidance(
                    "cwi-response-orchestrator",
                    "1.1.0",
                    "hash-1",
                    "Enforce risk response behavior.")
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("1. First risk.");
        context.Answer.Should().Contain("2. Second risk.");
        context.Answer.Should().NotContain("3. Third risk.");
        context.Answer.Should().Contain("Impact:");
        context.Answer.Should().Contain("Next action:");
    }

    [Fact]
    public async Task ExecuteAsync_enforces_daily_briefing_response_mode_and_limits()
    {
        var modelClient = new ThrowingModelClient();
        var step = new AskResponseOrchestrationStep(modelClient, JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "daily briefing",
            "enterprise",
            null,
            "observe",
            3)
        {
            Answer = """
                Priorities:
                1. First priority.
                2. Second priority.
                3. Third priority.
                4. Fourth priority.

                Risk:
                1. First risk.
                2. Second risk.

                Recommendation:
                1. First recommendation.
                2. Second recommendation.
                """,
            IntentType = "daily_briefing",
            TemplateUsed = "daily_briefing",
            BaselinePackGuidance = new[]
            {
                new BaselinePackGuidance(
                    "cwi-response-orchestrator",
                    "1.1.0",
                    "hash-1",
                    "Enforce daily briefing response behavior.")
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("1. First priority.");
        context.Answer.Should().Contain("3. Third priority.");
        context.Answer.Should().NotContain("4. Fourth priority.");
        context.Answer.Should().Contain("1. First risk.");
        context.Answer.Should().NotContain("2. Second risk.");
        context.Answer.Should().Contain("1. First recommendation.");
        context.Answer.Should().NotContain("2. Second recommendation.");
    }

    private sealed class ThrowingModelClient : IModelClient
    {
        public Task<ServiceResponse<ModelResult>> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            throw new Xunit.Sdk.XunitException("Response orchestration should not invoke the model client.");
        }
    }
}
