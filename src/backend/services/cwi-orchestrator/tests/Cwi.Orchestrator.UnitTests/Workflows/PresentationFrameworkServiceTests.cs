#nullable enable
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.TrustedWeb;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class PresentationFrameworkServiceTests
{
    [Fact]
    public void Evaluate_preserves_risk_response_template_shape()
    {
        var service = new PresentationFrameworkService();
        var context = Context("what is at risk right now");
        context.IntentType = "risk_review";
        context.TemplateUsed = "risk_summary";

        var decision = service.Evaluate(context, "Risks:\n1. Delivery slippage.\n\nImpact:\nThe launch could move.\n\nNext action:\nConfirm owners.");

        decision.FrameworkVersion.Should().Be(PresentationFrameworkService.Version);
        decision.TemplateFamily.Should().Be("risk");
        decision.RhythmMode.Should().Be("rhythm_structured");
        decision.FormattingMode.Should().Be("format_structured_sections");
        decision.SourceDisplayMode.Should().Be("source_display_none");
    }

    [Fact]
    public void Evaluate_preserves_code_blocks_as_runnable_artifacts()
    {
        var service = new PresentationFrameworkService();
        var context = Context("make me python code to reverse a string");
        context.IntentType = "code_generation";
        context.TemplateUsed = "code_answer";

        var decision = service.Evaluate(context, "```python\nprint('abc'[::-1])\n```");

        decision.TemplateFamily.Should().Be("code");
        decision.RhythmMode.Should().Be("rhythm_stepwise");
        decision.FormattingMode.Should().Be("format_code_preserve");
        decision.FormattingReason.Should().Be("format_code_blocks_must_remain_runnable");
    }

    [Fact]
    public void Evaluate_keeps_short_direct_answers_compact()
    {
        var service = new PresentationFrameworkService();
        var context = Context("what is the capital of Texas?");
        context.IntentType = "general_model_knowledge";
        context.QueryClassification = "deterministic_reasoning";
        context.TemplateUsed = "direct_answer";
        context.VerbosityMode = "concise";

        var decision = service.Evaluate(context, "Austin.");

        decision.TemplateFamily.Should().Be("direct_answer");
        decision.RhythmMode.Should().Be("rhythm_compact");
        decision.FormattingMode.Should().Be("format_compact");
        decision.SourceDisplayMode.Should().Be("source_display_none");
    }

    [Fact]
    public void Evaluate_keeps_memory_conflicts_visible_and_groups_memory_sources()
    {
        var service = new PresentationFrameworkService();
        var context = Context("what do you know about me?");
        context.IntentType = "memory_recall";
        context.TemplateUsed = "memory_profile";
        context.MemoryConflictSummaries = new[]
        {
            new MemoryConflictSummary
            {
                ConflictId = "conflict-1",
                Slot = "current_projects",
                MemoryIds = new[] { "mem-a", "mem-b" },
                Summary = "Two memories disagree about the active project."
            }
        };

        var decision = service.Evaluate(context, "I found a conflict in your memory.");

        decision.TemplateFamily.Should().Be("memory_backed");
        decision.FormattingMode.Should().Be("format_conflict_visible");
        decision.SourceDisplayMode.Should().Be("source_pills_grouped");
        decision.SourceDisplayReason.Should().Contain("memory");
    }

    [Fact]
    public void Evaluate_groups_web_and_document_sources_without_splitting_response_sections()
    {
        var service = new PresentationFrameworkService();
        var context = Context("use web to answer what is Austin?");
        context.TrustedWebResults = new[]
        {
            new TrustedWebResult
            {
                SourceId = "wikipedia",
                Title = "Austin, Texas",
                Url = "https://en.wikipedia.org/wiki/Austin,_Texas",
                ChunkId = "austin"
            }
        };
        context.SourceResults = new[]
        {
            new GovernedSourceResult
            {
                SourceId = "doc-1",
                ChunkId = "chunk-1",
                Text = "Austin is the capital of Texas."
            }
        };

        var decision = service.Evaluate(context, "Austin is the capital of Texas.");

        decision.TemplateFamily.Should().Be("source_backed");
        decision.FormattingMode.Should().Be("format_source_grouped");
        decision.SourceDisplayMode.Should().Be("source_pills_grouped");
        decision.SourceDisplayReason.Should().Contain("web");
        decision.SourceDisplayReason.Should().Contain("documents");
    }

    private static AskWorkflowContext Context(string question)
    {
        return new AskWorkflowContext("tenant", "user", question, "pack", null, "observe", 3)
        {
            QueryClassification = "general_reasoning",
            IntentType = "general_model_knowledge",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };
    }
}
#nullable disable
