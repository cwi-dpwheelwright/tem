using System;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Trace;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskDeterministicDirectRouteStepTests
{
    [Fact]
    public async Task ExecuteAsync_records_framework_overlays_for_deterministic_answers()
    {
        var step = new AskDeterministicDirectRouteStep(
            JourneyTestHelpers.CreateContext(),
            new TraceLookupStore());
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "whats todays dat",
            "personal",
            null,
            "observe",
            3,
            effectiveTimezone: "America/Chicago");

        var response = await step.ExecuteAsync(
            context,
            () => throw new Xunit.Sdk.XunitException("Deterministic answers should not call later workflow steps."),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Trace.Route.Should().Be("deterministic_direct_answer");
        response.Data.Trace.IntentType.Should().Be("current_date_intent");
        response.Data.Trace.ComprehensionExpressionVersion.Should().Be(ComprehensionExpressionFrameworkService.Version);
        response.Data.Trace.ContextAwarenessMode.Should().Be("direct_answer");
        response.Data.Trace.PresentationFrameworkVersion.Should().Be(PresentationFrameworkService.Version);
        response.Data.Trace.PresentationTemplateFamily.Should().Be("direct_answer");
    }

    [Fact]
    public void TryBuildDeterministicAnswer_answers_word_arithmetic()
    {
        var matched = AskDeterministicDirectRouteStep.TryBuildDeterministicAnswer(
            "what is 2 plus 2",
            "America/Chicago",
            new DateTimeOffset(2026, 4, 30, 10, 55, 0, TimeSpan.Zero),
            out var answer);

        matched.Should().BeTrue();
        answer.Answer.Should().Be("2 + 2 = 4.");
        answer.Classification.Should().Be("deterministic_reasoning");
        answer.RetrievalSkippedReason.Should().Be("deterministic_reasoning_does_not_require_sources");
    }

    [Fact]
    public void TryBuildDeterministicAnswer_answers_sum_wording()
    {
        var matched = AskDeterministicDirectRouteStep.TryBuildDeterministicAnswer(
            "what is the sum of 2 plus 2",
            "America/Chicago",
            new DateTimeOffset(2026, 4, 30, 10, 55, 0, TimeSpan.Zero),
            out var answer);

        matched.Should().BeTrue();
        answer.Answer.Should().Be("2 + 2 = 4.");
    }

    [Fact]
    public void TryBuildDeterministicAnswer_answers_localized_date_without_prior_context()
    {
        var prompt = """
            User: What is CWI?
            Assistant: CWI stands for Christian Wheelwright Intelligence.
            Current question: what is today's date
            """;

        var matched = AskDeterministicDirectRouteStep.TryBuildDeterministicAnswer(
            prompt,
            "America/Chicago",
            new DateTimeOffset(2026, 4, 30, 10, 55, 0, TimeSpan.Zero),
            out var answer);

        matched.Should().BeTrue();
        answer.Answer.Should().Be("Today is Thursday, April 30, 2026.");
        answer.Classification.Should().Be("deterministic_datetime");
        answer.IntentType.Should().Be("current_date_intent");
    }

    [Fact]
    public void TryBuildDeterministicAnswer_answers_localized_time()
    {
        var matched = AskDeterministicDirectRouteStep.TryBuildDeterministicAnswer(
            "what time is it in Houston",
            null,
            new DateTimeOffset(2026, 4, 30, 10, 55, 0, TimeSpan.Zero),
            out var answer);

        matched.Should().BeTrue();
        answer.Answer.Should().Be("It is 5:55 AM CDT.");
        answer.IntentType.Should().Be("current_time_intent");
    }

    [Fact]
    public void TryBuildDeterministicAnswer_answers_casual_date_time_wording()
    {
        var matched = AskDeterministicDirectRouteStep.TryBuildDeterministicAnswer(
            "i just want to know today's date and time",
            "America/Chicago",
            new DateTimeOffset(2026, 4, 30, 10, 55, 0, TimeSpan.Zero),
            out var answer);

        matched.Should().BeTrue();
        answer.Answer.Should().Be("It is Thursday, April 30, 2026 at 5:55 AM CDT.");
        answer.IntentType.Should().Be("current_datetime_intent");
    }

    [Theory]
    [InlineData("whats todays date", "Today is Thursday, April 30, 2026.", "current_date_intent")]
    [InlineData("whats todays dat", "Today is Thursday, April 30, 2026.", "current_date_intent")]
    [InlineData("wht time is it", "It is 5:55 AM CDT.", "current_time_intent")]
    [InlineData("wht is 2 plus 2", "2 + 2 = 4.", "deterministic_reasoning")]
    public void TryBuildDeterministicAnswer_normalizes_language_before_routing(
        string question,
        string expectedAnswer,
        string expectedIntent)
    {
        var matched = AskDeterministicDirectRouteStep.TryBuildDeterministicAnswer(
            question,
            "America/Chicago",
            new DateTimeOffset(2026, 4, 30, 10, 55, 0, TimeSpan.Zero),
            out var answer);

        matched.Should().BeTrue();
        answer.Answer.Should().Be(expectedAnswer);
        answer.IntentType.Should().Be(expectedIntent);
    }
}
