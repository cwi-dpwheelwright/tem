using Cwi.Orchestrator.Application.Ask;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Ask;

public sealed class ToolOrCurrentInfoPromptPolicyTests
{
    [Theory]
    [InlineData("whats teh wether tmrw do i need jaket", "weather.get")]
    [InlineData("wats tha newz today", "news.get")]
    [InlineData("who won teh game last nite", "sports.get")]
    [InlineData("wht do i have tomorow", "calendar.view")]
    [InlineData("reed my emails from chad", "email.read")]
    [InlineData("is cwi healthy", "system.status")]
    [InlineData("check all nodes and tell me if any are down", "node.health.check")]
    [InlineData("show me what actions happened today", "ledger.view")]
    [InlineData("look up the latest ai news", "search.web")]
    [InlineData("Use web and answer: what is the capital of Texas?", "search.web")]
    [InlineData("email chad and tell him we are good", "email.send")]
    [InlineData("start the workflow now", "workflow.start")]
    public void TryClassifyDirectToolIntent_maps_tool_and_current_info_prompts(string input, string expectedIntent)
    {
        var matched = ToolOrCurrentInfoPromptPolicy.TryClassifyDirectToolIntent(input, out var match);

        matched.Should().BeTrue();
        match.IntentType.Should().Be(expectedIntent);
        match.RetrievalDomain.Should().NotBeNullOrWhiteSpace();
        match.RetrievalDomainTerms.Should().NotBeEmpty();
        match.Reason.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("current supplier status in 2026")]
    [InlineData("what are the latest topics in operations")]
    public void TryClassifySearchFallbackIntent_maps_current_info_without_entity_lookup(string input)
    {
        var matched = ToolOrCurrentInfoPromptPolicy.TryClassifySearchFallbackIntent(input, out var match);

        matched.Should().BeTrue();
        match.IntentType.Should().Be("search.web");
        ToolOrCurrentInfoPromptPolicy.IsRouteProtectionMatch(input).Should().BeTrue();
    }

    [Theory]
    [InlineData("What is the population of Texas?")]
    [InlineData("what is oceaneering")]
    [InlineData("who is random two-word name")]
    [InlineData("send to jon")]
    public void IsRouteProtectionMatch_does_not_absorb_non_tool_prompts(string input)
    {
        ToolOrCurrentInfoPromptPolicy.IsRouteProtectionMatch(input).Should().BeFalse();
    }

    [Fact]
    public void TryClassifyDirectToolIntent_respects_web_off_directive()
    {
        var matched = ToolOrCurrentInfoPromptPolicy.TryClassifyDirectToolIntent(
            "Web is off. Tell me the latest Houston Rockets news.",
            out _);

        matched.Should().BeFalse();
    }
}
