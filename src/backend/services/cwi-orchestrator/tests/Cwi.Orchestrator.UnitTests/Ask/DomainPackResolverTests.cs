using Cwi.Orchestrator.Application.Ask;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Ask;

public sealed class DomainPackResolverTests
{
    [Fact]
    public void Resolve_selects_home_node_pack_for_home_node_queries()
    {
        var pack = DomainPackResolver.Resolve("How does the home node work?", "auto", null);

        pack.Should().Be("home-node-pack");
    }

    [Fact]
    public void Resolve_selects_engineering_pack_for_engineering_queries()
    {
        var pack = DomainPackResolver.Resolve("Explain CWI to an engineer.", "general-pack", null);

        pack.Should().Be("engineering-pack");
    }

    [Fact]
    public void Resolve_honors_explicit_pack_names()
    {
        var pack = DomainPackResolver.Resolve("How does the home node work?", "custom-pack", null);

        pack.Should().Be("custom-pack");
    }

    [Fact]
    public void Resolve_honors_anchor_pinned_requests()
    {
        var pack = DomainPackResolver.Resolve("How does the home node work?", "general-pack", "anchor-1");

        pack.Should().Be("general-pack");
    }

    [Fact]
    public void Resolve_uses_primary_question_instead_of_recent_chat_context()
    {
        var question = """
            Primary question:
            yo this is broken

            The primary question is authoritative. Use the user-provided context below only when it directly supports the primary question.

            Conversation context from this chat:
            Turn 1
            User: How does the home node work?
            Assistant: A CWI home node runs close to the user.
            """;

        var pack = DomainPackResolver.Resolve(question, "auto", null);

        pack.Should().Be("general-pack");
    }
}
