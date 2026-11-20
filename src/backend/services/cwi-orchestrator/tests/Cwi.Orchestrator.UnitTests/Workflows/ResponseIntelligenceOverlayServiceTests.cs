#nullable enable
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class ResponseIntelligenceOverlayServiceTests
{
    [Fact]
    public void Apply_populates_framework_decisions_without_changing_native_execution_controls()
    {
        var service = new ResponseIntelligenceOverlayService();
        var context = new AskWorkflowContext("tenant", "user", "what is at risk right now", "pack", null, "observe", 3)
        {
            Answer = "Risks:\n1. Delivery risk.\n\nImpact:\nLaunch could move.\n\nNext action:\nConfirm owners.",
            IntentType = "risk_review",
            QueryClassification = "proactive_intelligence",
            TemplateUsed = "risk_summary",
            RetrievalAllowed = false,
            RetrievalSkippedReason = "proactive_mode_no_governed_memory_available",
            GroundingMode = "non_grounded",
            ExecutionTarget = "shared_compute",
            McpPolicyDecision = "blocked",
            McpExecutionStatus = "blocked_by_policy"
        };

        service.Apply(context);

        context.ComprehensionExpressionVersion.Should().Be(ComprehensionExpressionFrameworkService.Version);
        context.ContextAwarenessMode.Should().Be("structured_report");
        context.ClarityDecision.Should().Be("clarity_mode_shape_enforced");
        context.ExpressionToneProfile.Should().NotBeNullOrWhiteSpace();
        context.PresentationFrameworkVersion.Should().Be(PresentationFrameworkService.Version);
        context.PresentationTemplateFamily.Should().Be("risk");
        context.PresentationRhythmMode.Should().Be("rhythm_structured");
        context.PresentationFormattingMode.Should().Be("format_structured_sections");
        context.ChatResponseQualityVersion.Should().Be(ChatResponseQualityService.Version);
        context.ChatResponseProfile.Should().Be("chat_action_boundary");
        context.ChatContextBudgetMode.Should().Be("context_budget_default");
        context.ChatFinalPolishApplied.Should().BeNull();

        context.TemplateUsed.Should().Be("risk_summary");
        context.IntentType.Should().Be("risk_review");
        context.RetrievalAllowed.Should().BeFalse();
        context.RetrievalSkippedReason.Should().Be("proactive_mode_no_governed_memory_available");
        context.GroundingMode.Should().Be("non_grounded");
        context.ExecutionTarget.Should().Be("shared_compute");
        context.McpPolicyDecision.Should().Be("blocked");
        context.McpExecutionStatus.Should().Be("blocked_by_policy");
    }

    [Fact]
    public void Apply_keeps_existing_tone_for_memory_without_forcing_sources()
    {
        var service = new ResponseIntelligenceOverlayService();
        var context = new AskWorkflowContext("tenant", "user", "what do you know about me?", "pack", null, "observe", 3)
        {
            Answer = "Here is what I understand about you.",
            IntentType = "memory_recall",
            QueryClassification = "memory_graph_request",
            TemplateUsed = "memory_profile",
            ToneMode = "memory_concise",
            MemoryActiveContextCount = 2
        };

        service.Apply(context);

        context.ToneMode.Should().Be("memory_concise");
        context.ContextAwarenessMode.Should().Be("memory");
        context.PresentationTemplateFamily.Should().Be("memory_backed");
        context.PresentationSourceDisplayMode.Should().Be("source_display_none");
        context.ChatResponseProfile.Should().Be("chat_memory_recall");
        context.ChatContextBudgetMode.Should().Be("context_budget_memory_scoped");
    }

    [Fact]
    public void Apply_polishes_backend_memory_prefaces_without_removing_memory_answer()
    {
        var service = new ResponseIntelligenceOverlayService();
        var context = new AskWorkflowContext("tenant", "user", "what do you know about me?", "pack", null, "observe", 3)
        {
            Answer = "Here is what I know from activated user memory:\n\n- You are a software engineer.",
            IntentType = "memory_recall",
            QueryClassification = "memory_graph_request",
            TemplateUsed = "memory_profile",
            MemoryActiveContextCount = 1
        };

        service.Apply(context);

        context.Answer.Should().Be("Here is what I understand:\n\n- You are a software engineer.");
        context.ChatFinalPolishApplied.Should().Be(true);
        context.ChatResponseProfile.Should().Be("chat_memory_recall");
    }

    [Fact]
    public void Apply_normalizes_escaped_formatting_for_chat_answers_without_touching_code_blocks()
    {
        var service = new ResponseIntelligenceOverlayService();
        var context = new AskWorkflowContext("tenant", "user", "what do you understand about me?", "pack", null, "observe", 3)
        {
            Answer = "Identity:\\n- Christian works in operations.\\n\\nPriorities:\\n- Validate response quality.",
            IntentType = "memory_recall",
            QueryClassification = "memory_graph_request",
            TemplateUsed = "memory_profile",
            MemoryActiveContextCount = 1
        };

        service.Apply(context);

        context.Answer.Should().Be("Identity:\n- Christian works in operations.\n\nPriorities:\n- Validate response quality.");
        context.ChatFinalPolishApplied.Should().Be(true);
    }

    [Fact]
    public void Apply_rewrites_backend_memory_opening_sentences()
    {
        var service = new ResponseIntelligenceOverlayService();
        var context = new AskWorkflowContext("tenant", "user", "describe me as a person", "pack", null, "observe", 3)
        {
            Answer = "Based on activated imported memory, I can describe what your current context suggests about you.",
            IntentType = "memory_recall",
            QueryClassification = "memory_graph_request",
            TemplateUsed = "memory_profile",
            MemoryActiveContextCount = 1
        };

        service.Apply(context);

        context.Answer.Should().Be("Here is what I understand:\n\nI can describe what your current context suggests about you.");
        context.ChatFinalPolishApplied.Should().Be(true);
    }

    [Fact]
    public void Apply_preserves_literal_escaped_characters_inside_code_blocks()
    {
        var service = new ResponseIntelligenceOverlayService();
        var context = new AskWorkflowContext("tenant", "user", "make python print newline", "pack", null, "observe", 3)
        {
            Answer = "```python\nprint(\"hello\\nworld\")\n```",
            IntentType = "code_generation",
            TemplateUsed = "code_answer"
        };

        service.Apply(context);

        context.Answer.Should().Be("```python\nprint(\"hello\\nworld\")\n```");
        context.ChatFinalPolishApplied.Should().BeNull();
        context.ChatResponseProfile.Should().Be("chat_code");
    }

    [Fact]
    public void Apply_normalizes_common_utf8_mojibake_in_chat_text()
    {
        var service = new ResponseIntelligenceOverlayService();
        var context = new AskWorkflowContext("tenant", "user", "what should I say?", "pack", null, "observe", 3)
        {
            Answer = "You could say: \"I see that you said it\u00e2\u20ac\u2122s fine\u00e2\u20ac\u201dcan we talk?\"",
            IntentType = "decision_support",
            TemplateUsed = "general_answer"
        };

        service.Apply(context);

        context.Answer.Should().Be("You could say: \"I see that you said it's fine - can we talk?\"");
        context.ChatFinalPolishApplied.Should().Be(true);
    }

    [Fact]
    public void Apply_marks_message_drafts_as_sendable_first_responses()
    {
        var service = new ResponseIntelligenceOverlayService();
        var context = new AskWorkflowContext("tenant", "user", "I need to explain why I missed something without making excuses.", "pack", null, "observe", 3)
        {
            Answer = "I am sorry I missed this. I should have followed up sooner, and I will get it handled today.",
            IntentType = "message_draft",
            QueryClassification = "general_reasoning"
        };

        service.Apply(context);

        context.ContextAwarenessMode.Should().Be("communication_draft");
        context.ExpressionToneProfile.Should().Be("tone_respectful_concise");
        context.PresentationTemplateFamily.Should().Be("message_draft");
        context.PresentationFormattingMode.Should().Be("format_message_first");
        context.ChatResponseProfile.Should().Be("chat_message_draft");
        context.ChatContextBudgetMode.Should().Be("context_budget_default");
    }

    [Fact]
    public void Apply_marks_everyday_guidance_as_practical_chat()
    {
        var service = new ResponseIntelligenceOverlayService();
        var context = new AskWorkflowContext("tenant", "user", "I have 25 minutes before practice - should I eat, shower, or pack first?", "pack", null, "observe", 3)
        {
            Answer = "Pack first, then grab a quick snack.",
            IntentType = "practical_guidance",
            QueryClassification = "general_reasoning"
        };

        service.Apply(context);

        context.ContextAwarenessMode.Should().Be("practical_guidance");
        context.PresentationTemplateFamily.Should().Be("practical_guidance");
        context.ChatResponseProfile.Should().Be("chat_practical_guidance");
    }

    [Theory]
    [InlineData("Sure, here's a short message:\n\nI am sorry for snapping earlier. That was not fair to you.", "I am sorry for snapping earlier.")]
    [InlineData("Draft message: I cannot lend money right now, but I hope you understand.", "I cannot lend money right now")]
    [InlineData("You could say: Before I start, can you clarify what outcome you want?", "Before I start")]
    public void Apply_removes_message_draft_wrappers_so_the_draft_is_first(string answer, string expectedStart)
    {
        var service = new ResponseIntelligenceOverlayService();
        var context = new AskWorkflowContext("tenant", "user", "I need to apologize for snapping earlier.", "pack", null, "observe", 3)
        {
            Answer = answer,
            IntentType = "message_draft",
            QueryClassification = "general_reasoning"
        };

        service.Apply(context);

        context.Answer.Should().StartWith(expectedStart);
        context.Answer.Should().NotStartWith("Sure");
        context.Answer.Should().NotStartWith("Draft message");
        context.Answer.Should().NotStartWith("You could say");
        context.ChatFinalPolishApplied.Should().Be(true);
    }
}
#nullable disable
