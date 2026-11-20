using Cwi.Common;
using Cwi.Orchestrator.Application.Graph;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Graph;

public sealed class UserIntelligenceServiceTests
{
    [Fact]
    public async Task GetSummaryAsyncBuildsWeeklyGraphBackedSections()
    {
        var provider = new FakeGraphEvidenceProvider();
        var service = new UserIntelligenceService(new GraphQueryService(provider), new UserGraphProfileService(provider));

        var result = await service.GetSummaryAsync("tenant-1", "user-1", "weekly", 3, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Period.Should().Be("weekly");
        result.Data.Sections.Select(section => section.Name)
            .Should()
            .Contain(new[] { "current_work", "goals", "open_loops", "home_life", "work_life" });
        result.Data.Sections.Should().OnlyContain(section => section.Items.Count > 0);
    }

    [Fact]
    public async Task GetSuggestionsAsyncPromotesDeadlineOpenLoops()
    {
        var provider = new FakeGraphEvidenceProvider();
        var service = new UserIntelligenceService(new GraphQueryService(provider), new UserGraphProfileService(provider));

        var result = await service.GetSuggestionsAsync("tenant-1", "user-1", 5, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Suggestions.Should().Contain(item =>
            item.Type == "next_action"
            && item.Priority == "high"
            && item.Rationale.Contains("deadline", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetRisksAsyncReturnsRankedRiskSignals()
    {
        var provider = new FakeGraphEvidenceProvider();
        var service = new UserIntelligenceService(new GraphQueryService(provider), new UserGraphProfileService(provider));

        var result = await service.GetRisksAsync("tenant-1", "user-1", 5, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Risks.Should().Contain(item =>
            item.Type == "risk"
            && item.Priority == "high"
            && item.Detail.Contains("blocked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetPlanAsyncRanksDeadlineAndRiskItemsFirst()
    {
        var provider = new FakeGraphEvidenceProvider();
        var service = new UserIntelligenceService(new GraphQueryService(provider), new UserGraphProfileService(provider));

        var result = await service.GetPlanAsync("tenant-1", "user-1", 5, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Items.Should().NotBeEmpty();
        result.Data.Items[0].Priority.Should().Be("high");
        result.Data.Items[0].PriorityScore.Should().BeGreaterThan(0.70);
    }

    [Fact]
    public async Task GetBriefingAsyncCombinesSummaryChangesRisksSuggestionsAndPlan()
    {
        var provider = new FakeGraphEvidenceProvider();
        var service = new UserIntelligenceService(new GraphQueryService(provider), new UserGraphProfileService(provider));

        var result = await service.GetBriefingAsync("tenant-1", "user-1", 5, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.DailySummary.Should().NotBeNull();
        result.Data.Changes.Should().NotBeEmpty();
        result.Data.Risks.Should().NotBeEmpty();
        result.Data.Suggestions.Should().NotBeEmpty();
        result.Data.Plan.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetSummaryAsyncRequiresTenantScope()
    {
        var provider = new FakeGraphEvidenceProvider();
        var service = new UserIntelligenceService(new GraphQueryService(provider), new UserGraphProfileService(provider));

        var result = await service.GetSummaryAsync("", "user-1", "daily", 3, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(400);
    }

    private sealed class FakeGraphEvidenceProvider : IGraphEvidenceProvider
    {
        public Task<ServiceResponse<GraphQueryResult>> RetrieveAsync(GraphEvidenceRequest request, CancellationToken cancellationToken)
        {
            var template = ResolveTemplate(request.Question);
            IReadOnlyList<GraphEvidence> evidence = new[]
            {
                new GraphEvidence
                {
                    Subject = template,
                    Relation = "REMEMBERED",
                    Object = ResolveObject(template),
                    Template = template,
                    SourceDocument = "source-1",
                    SourcePackTarget = "chatgpt_import",
                    TargetPackTarget = "cwi_memory_graph"
                }
            };

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

            if (lower.Contains("home life", StringComparison.Ordinal))
            {
                return "home_life";
            }

            if (lower.Contains("work life", StringComparison.Ordinal))
            {
                return "work_life";
            }

            if (lower.Contains("decision", StringComparison.Ordinal))
            {
                return "decisions";
            }

            return "current_work";
        }

        private static string ResolveObject(string template)
        {
            return template switch
            {
                "open_loops" => "Follow up on the investor demo deadline this week.",
                "risks" => "Ticket CWI-102 is blocked by a missing API token and creates deadline risk.",
                "recent_changes" => "New billing connector record: car insurance bill due Friday.",
                "priorities" => "Client deadline follow-up is blocked and due this week.",
                _ => $"Remembered {template} fact."
            };
        }
    }
}
