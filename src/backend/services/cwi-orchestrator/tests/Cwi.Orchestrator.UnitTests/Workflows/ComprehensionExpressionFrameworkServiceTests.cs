#nullable enable
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class ComprehensionExpressionFrameworkServiceTests
{
    [Fact]
    public void Evaluate_marks_simple_questions_as_answer_first_direct_context()
    {
        var service = new ComprehensionExpressionFrameworkService();
        var context = Context("What is the capital of Texas?", intentType: "general_model_knowledge");

        var decision = service.Evaluate(context);

        decision.FrameworkVersion.Should().Be(ComprehensionExpressionFrameworkService.Version);
        decision.ContextMode.Should().Be("direct_answer");
        decision.ClarityDecision.Should().Be("clarity_answer_first");
        decision.ToneProfile.Should().Be("tone_default_helpful");
    }

    [Fact]
    public void Evaluate_honors_executive_context_without_changing_response_mode()
    {
        var service = new ComprehensionExpressionFrameworkService();
        var context = Context("Give me an email update for the CEO.", intentType: "decision_support");

        var decision = service.Evaluate(context);

        decision.ContextMode.Should().Be("executive");
        decision.ToneProfile.Should().Be("tone_executive");
        decision.ToneReason.Should().Be("tone_explicit_user_request");
        decision.AudienceMode.Should().Be("executive");
        decision.VerbosityMode.Should().Be("concise");
    }

    [Fact]
    public void Evaluate_forces_sensitive_tone_for_safety_or_secret_topics()
    {
        var service = new ComprehensionExpressionFrameworkService();
        var context = Context("Explain this API key leak casually.", intentType: "general_model_knowledge");

        var decision = service.Evaluate(context);

        decision.ContextMode.Should().Be("sensitive");
        decision.ToneProfile.Should().Be("tone_sensitive");
        decision.ToneReason.Should().Be("tone_safety_override");
    }

    [Fact]
    public void Evaluate_preserves_structured_response_mode_shape()
    {
        var service = new ComprehensionExpressionFrameworkService();
        var context = Context("what is at risk right now", intentType: "risk_review");
        context.QueryClassification = "memory_graph_request";
        context.EvidenceRequired = true;
        context.RetrievalAllowed = true;

        var decision = service.Evaluate(context);

        decision.ContextMode.Should().Be("structured_report");
        decision.ClarityDecision.Should().Be("clarity_mode_shape_enforced");
        decision.ClarityReason.Should().Be("response_mode_template_controls_shape");
    }

    [Fact]
    public void Evaluate_preserves_memory_conflict_ambiguity()
    {
        var service = new ComprehensionExpressionFrameworkService();
        var context = Context("what do you know about me?", intentType: "memory_recall");
        context.QueryClassification = "memory_graph_request";
        context.MemoryConflictSummaries = new[]
        {
            new MemoryConflictSummary
            {
                ConflictId = "conflict-1",
                Slot = "projects",
                MemoryIds = new[] { "mem-a", "mem-b" }
            }
        };

        var decision = service.Evaluate(context);

        decision.ContextMode.Should().Be("memory");
        decision.ClarityDecision.Should().Be("clarity_ambiguity_preserved");
        decision.ClarityReason.Should().Be("memory_conflict_must_remain_visible");
    }

    private static AskWorkflowContext Context(string question, string intentType)
    {
        return new AskWorkflowContext("tenant", "user", question, "pack", null, "observe", 3)
        {
            QueryClassification = "general_reasoning",
            IntentType = intentType,
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };
    }
}
#nullable disable
