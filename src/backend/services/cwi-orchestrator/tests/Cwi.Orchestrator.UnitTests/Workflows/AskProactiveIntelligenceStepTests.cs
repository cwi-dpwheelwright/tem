using Cwi.Common;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskProactiveIntelligenceStepTests
{
    [Fact]
    public async Task ExecuteAsync_daily_briefing_calls_user_intelligence_and_shapes_briefing()
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", "daily briefing", "enterprise", null, "observe", 5)
        {
            QueryClassification = "proactive_intelligence_request",
            IntentType = "daily_briefing",
            RetrievalAllowed = false,
            GroundingMode = "proactive_intelligence"
        };
        var nextCalled = false;

        var response = await step.ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.Answer.Should().Contain("Priorities:");
        context.Answer.Should().Contain("Risk:");
        context.Answer.Should().Contain("Recommendation:");
        CountItemsInSection(context.Answer!, "Priorities").Should().BeLessThanOrEqualTo(3);
        CountItemsInSection(context.Answer!, "Risk").Should().Be(1);
        CountItemsInSection(context.Answer!, "Recommendation").Should().Be(1);
        context.Route.Should().Be("proactive_intelligence");
        context.TemplateUsed.Should().Be("daily_briefing");
        context.SkipModelInference.Should().BeTrue();
        context.RetrievalAllowed.Should().BeFalse();
        context.EvidenceRequired.Should().BeFalse();
        context.ModelName.Should().Be("user_intelligence.briefing");
    }

    [Fact]
    public async Task ExecuteAsync_risk_review_calls_user_intelligence_and_limits_risks()
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", "what's at risk right now", "enterprise", null, "observe", 5)
        {
            QueryClassification = "proactive_intelligence_request",
            IntentType = "risk_review"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Risks:");
        context.Answer.Should().Contain("Impact:");
        context.Answer.Should().Contain("Next action:");
        CountItemsInSection(context.Answer!, "Risks").Should().BeLessThanOrEqualTo(2);
        CountItemsInSection(context.Answer!, "Suggestions").Should().BeLessThanOrEqualTo(3);
        context.TemplateUsed.Should().Be("risk_summary");
        context.ToneMode.Should().Be("risk_concise");
        context.ExecutionTarget.Should().Be("proactive_intelligence_api");
    }

    [Theory]
    [InlineData("whats at rsk with this plan", "risk_analysis", "risk_summary")]
    [InlineData("hlp me make a plan for this", "planning", "plan_summary")]
    public async Task ExecuteAsync_accepts_p0_alias_intents_for_proactive_modes(
        string question,
        string intent,
        string expectedTemplate)
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", question, "enterprise", null, "observe", 5)
        {
            QueryClassification = "proactive_intelligence_request",
            IntentType = intent
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.TemplateUsed.Should().Be(expectedTemplate);
        context.Route.Should().Be("proactive_intelligence");
        context.SkipModelInference.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_plan_mode_calls_user_intelligence_and_limits_priorities()
    {
        var step = CreateStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", "Make a plan", "enterprise", null, "observe", 8)
        {
            QueryClassification = "proactive_intelligence_request",
            IntentType = "plan_request"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Priorities:");
        context.Answer.Should().Contain("Risks:");
        context.Answer.Should().Contain("Suggestions:");
        CountItemsInSection(context.Answer!, "Priorities").Should().BeLessThanOrEqualTo(5);
        CountItemsInSection(context.Answer!, "Risks").Should().BeLessThanOrEqualTo(2);
        CountItemsInSection(context.Answer!, "Suggestions").Should().BeLessThanOrEqualTo(3);
        context.TemplateUsed.Should().Be("plan_summary");
        context.ToneMode.Should().Be("plan_concise");
    }

    [Fact]
    public async Task ExecuteAsync_daily_briefing_empty_state_uses_user_facing_language()
    {
        var step = CreateEmptyStep();
        var context = new AskWorkflowContext("tenant-1", "user-1", "daily briefing", "enterprise", null, "observe", 5)
        {
            QueryClassification = "proactive_intelligence_request",
            IntentType = "daily_briefing",
            RetrievalAllowed = false,
            GroundingMode = "proactive_intelligence"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("calendar, task, or reminder details");
        context.Answer.Should().NotContain("synced memory");
        context.Answer.Should().NotContain("graph data");
        context.Answer.Should().NotContain("memory or graph");
        context.SkipModelInference.Should().BeTrue();
    }

    private static AskProactiveIntelligenceStep CreateStep()
    {
        var provider = new FakeGraphEvidenceProvider();
        var service = new UserIntelligenceService(new GraphQueryService(provider), new UserGraphProfileService(provider));
        return new AskProactiveIntelligenceStep(service, JourneyTestHelpers.CreateEmitter());
    }

    private static AskProactiveIntelligenceStep CreateEmptyStep()
    {
        var provider = new EmptyGraphEvidenceProvider();
        var service = new UserIntelligenceService(new GraphQueryService(provider), new UserGraphProfileService(provider));
        return new AskProactiveIntelligenceStep(service, JourneyTestHelpers.CreateEmitter());
    }

    private static int CountItemsInSection(string answer, string sectionTitle)
    {
        var count = 0;
        var active = false;
        foreach (var line in answer.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.EndsWith(":", StringComparison.Ordinal))
            {
                active = trimmed.TrimEnd(':').Equals(sectionTitle, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (active && trimmed.Length >= 3 && char.IsDigit(trimmed[0]) && trimmed[1] == '.')
            {
                count++;
            }
        }

        return count;
    }

    private sealed class FakeGraphEvidenceProvider : IGraphEvidenceProvider
    {
        public Task<ServiceResponse<GraphQueryResult>> RetrieveAsync(GraphEvidenceRequest request, CancellationToken cancellationToken)
        {
            var template = ResolveTemplate(request.Question);
            var evidence = Enumerable.Range(1, ResolveCount(template))
                .Select(index => new GraphEvidence
                {
                    Subject = template,
                    Relation = "REMEMBERED",
                    Object = ResolveObject(template, index),
                    Template = template,
                    SourceDocument = $"source-{index}",
                    SourcePackTarget = "chatgpt_import",
                    TargetPackTarget = "cwi_memory_graph"
                })
                .ToArray();

            return Task.FromResult(ServiceResponse<GraphQueryResult>.Success(new GraphQueryResult
            {
                Template = template,
                Terms = new[] { template },
                Evidence = evidence
            }));
        }

        private static string ResolveTemplate(string question)
        {
            var lower = question.ToLowerInvariant();
            if (lower.Contains("risk", StringComparison.Ordinal))
            {
                return "risks";
            }

            if (lower.Contains("changed", StringComparison.Ordinal))
            {
                return "recent_changes";
            }

            if (lower.Contains("focus", StringComparison.Ordinal))
            {
                return "priorities";
            }

            if (lower.Contains("open loop", StringComparison.Ordinal))
            {
                return "open_loops";
            }

            if (lower.Contains("goal", StringComparison.Ordinal))
            {
                return "goals";
            }

            if (lower.Contains("decision", StringComparison.Ordinal))
            {
                return "decisions";
            }

            return "current_work";
        }

        private static int ResolveCount(string template)
        {
            return template switch
            {
                "priorities" => 6,
                "risks" => 3,
                "open_loops" => 4,
                "goals" => 4,
                _ => 2
            };
        }

        private static string ResolveObject(string template, int index)
        {
            return template switch
            {
                "open_loops" => $"Follow up on open loop {index} before the deadline this week.",
                "goals" => $"Review progress toward goal {index}.",
                "risks" => $"Risk {index}: blocked dependency could affect the deadline.",
                "recent_changes" => $"Recent change {index}: connector data changed today.",
                "priorities" => $"Priority {index}: blocked client follow-up due this week.",
                _ => $"Remembered {template} fact {index}."
            };
        }
    }

    private sealed class EmptyGraphEvidenceProvider : IGraphEvidenceProvider
    {
        public Task<ServiceResponse<GraphQueryResult>> RetrieveAsync(GraphEvidenceRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ServiceResponse<GraphQueryResult>.Success(new GraphQueryResult
            {
                Template = "current_work",
                Terms = Array.Empty<string>(),
                Evidence = Array.Empty<GraphEvidence>()
            }));
        }
    }
}
