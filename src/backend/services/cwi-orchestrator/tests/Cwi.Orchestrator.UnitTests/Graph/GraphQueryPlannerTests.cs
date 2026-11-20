using Cwi.Orchestrator.Application.Graph;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Graph;

public sealed class GraphQueryPlannerTests
{
    [Fact]
    public void TryPlan_routes_current_work_questions_to_user_memory_graph()
    {
        var planner = new GraphQueryPlanner();

        var plan = planner.TryPlan("What have I been working on?", 5, "tenant-1", "user-1");

        plan.Should().NotBeNull();
        plan!.Template.Should().Be("current_work");
        plan.Terms.Should().Contain("work");
        plan.Query.Should().Contain("MATCH (u:user)-[:HAS_MEMORY]->(m)");
        plan.Query.Should().Contain("memory_type");
        plan.Query.Should().Contain("tenant-1");
        plan.Query.Should().Contain("user-1");
    }

    [Fact]
    public void TryPlan_routes_risk_questions_to_connector_aware_memory_graph()
    {
        var planner = new GraphQueryPlanner();

        var plan = planner.TryPlan("Are there any risks I should know about?", 5, "tenant-1", "user-1");

        plan.Should().NotBeNull();
        plan!.Template.Should().Be("risks");
        plan.Query.Should().Contain("issue_memory");
        plan.Query.Should().Contain("financial_memory");
        plan.Query.Should().Contain("deadline");
    }

    [Fact]
    public void TryPlan_routes_priority_questions_to_rankable_memory_graph()
    {
        var planner = new GraphQueryPlanner();

        var plan = planner.TryPlan("What should I focus on first?", 5, "tenant-1", "user-1");

        plan.Should().NotBeNull();
        plan!.Template.Should().Be("priorities");
        plan.Query.Should().Contain("task_memory");
        plan.Query.Should().Contain("financial_memory");
    }

    [Fact]
    public void TryPlan_prefers_specific_home_life_rule_over_general_profile_rule()
    {
        var planner = new GraphQueryPlanner();

        var plan = planner.TryPlan("What do you know about my home life?", 5, "tenant-1", "user-1");

        plan.Should().NotBeNull();
        plan!.Template.Should().Be("home_life");
        plan.Terms.Should().Contain("home");
        plan.Query.Should().Contain("food_meals");
        plan.Query.Should().Contain("health_wellness");
    }

    [Fact]
    public void TryPlan_can_use_injected_rule_without_modifying_planner()
    {
        var rule = new RegexGraphQueryTemplateRule(
            order: 1,
            template: "custom_memory",
            usesUserMemory: true,
            defaultTerms: new[] { "custom" },
            categoryFilter: "coalesce(m.memory_category, '') = 'custom'",
            patterns: new[] { @"\bcustom\s+memory\b" });
        var planner = new GraphQueryPlanner(new[] { rule });

        var plan = planner.TryPlan("Show custom memory.", 3, "tenant-1", "user-1");

        plan.Should().NotBeNull();
        plan!.Template.Should().Be("custom_memory");
        plan.Terms.Should().Contain("custom");
        plan.Query.Should().Contain("memory_category");
        plan.Query.Should().Contain("'custom'");
    }

    [Fact]
    public void TryPlan_does_not_plan_user_memory_query_without_tenant_scope()
    {
        var planner = new GraphQueryPlanner();

        var plan = planner.TryPlan("What are my open loops?", 5);

        plan.Should().BeNull();
    }
}
