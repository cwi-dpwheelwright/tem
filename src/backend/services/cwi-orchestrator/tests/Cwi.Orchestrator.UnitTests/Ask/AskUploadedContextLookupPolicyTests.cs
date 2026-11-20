using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Workflows;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Ask;

public sealed class AskUploadedContextLookupPolicyTests
{
    [Fact]
    public void TryApply_enables_user_context_retrieval_for_plain_entity_question()
    {
        var context = GeneralKnowledgeContext("What is Polaris?");

        var applied = AskUploadedContextLookupPolicy.TryApply(context, out var snapshot);

        applied.Should().BeTrue();
        snapshot.QueryClassification.Should().Be("general_model_knowledge");
        context.QueryClassification.Should().Be("uploaded_context_lookup");
        context.IntentType.Should().Be("context_lookup");
        context.RetrievalAllowed.Should().BeTrue();
        context.GroundingMode.Should().Be("source_grounded");
        context.RetrievalDomain.Should().Be("uploaded_user_context");
        context.RetrievalDomainTerms.Should().ContainSingle("polaris");
        context.EvidenceRequired.Should().BeFalse();
    }

    [Fact]
    public void TryApply_extracts_entity_terms_without_instruction_words()
    {
        var context = GeneralKnowledgeContext("Can you describe Polaris, the platform developed by Astrion9?");

        AskUploadedContextLookupPolicy.TryApply(context, out _).Should().BeTrue();

        context.RetrievalDomainTerms.Should().Contain("polaris");
        context.RetrievalDomainTerms.Should().Contain("astrion9");
        context.RetrievalDomainTerms.Should().NotContain("describe");
        context.RetrievalDomainTerms.Should().NotContain("platform");
    }

    [Fact]
    public void TryApply_handles_explain_prompt_variants_and_duplicate_words()
    {
        var context = GeneralKnowledgeContext("Can you you explain to me what the platform Polaris is, that is made by Astrion9?");

        AskUploadedContextLookupPolicy.TryApply(context, out _).Should().BeTrue();

        context.RetrievalDomainTerms.Should().Contain("polaris");
        context.RetrievalDomainTerms.Should().Contain("astrion9");
        context.RetrievalDomainTerms.Should().NotContain("explain");
        context.RetrievalDomainTerms.Should().NotContain("platform");
        context.RetrievalDomainTerms.Should().NotContain("made");
    }

    [Theory]
    [InlineData("What is the capital of Texas?")]
    [InlineData("What is the capital of France?")]
    [InlineData("capital of Texas")]
    public void TryApply_does_not_override_plain_public_fact_questions(string question)
    {
        var context = GeneralKnowledgeContext(question);

        AskUploadedContextLookupPolicy.TryApply(context, out _).Should().BeFalse();

        context.QueryClassification.Should().Be("general_model_knowledge");
        context.IntentType.Should().Be("explanation");
        context.RetrievalAllowed.Should().BeFalse();
        context.GroundingMode.Should().Be("non_grounded");
        context.RetrievalSkippedReason.Should().Be("general_knowledge_does_not_require_enterprise_retrieval");
    }

    [Fact]
    public void TryApply_does_not_require_lookup_trigger_phrasing()
    {
        var context = GeneralKnowledgeContext("Polaris Astrion9 platform details");

        AskUploadedContextLookupPolicy.TryApply(context, out _).Should().BeTrue();

        context.RetrievalDomainTerms.Should().Contain("polaris");
        context.RetrievalDomainTerms.Should().Contain("astrion9");
        context.RetrievalDomainTerms.Should().Contain("details");
    }

    [Fact]
    public void TryApply_does_not_override_non_general_routes()
    {
        var context = GeneralKnowledgeContext("What is Polaris?");
        context.QueryClassification = "summarization";
        context.IntentType = "summarization";

        AskUploadedContextLookupPolicy.TryApply(context, out _).Should().BeFalse();
        context.QueryClassification.Should().Be("summarization");
        context.RetrievalAllowed.Should().BeFalse();
    }

    [Fact]
    public void TryApply_does_not_treat_self_context_disclosures_as_uploaded_context_lookup()
    {
        var context = GeneralKnowledgeContext("My name is Demitrius Wheelwright and I am a Software Engineer");

        AskUploadedContextLookupPolicy.TryApply(context, out _).Should().BeFalse();

        context.QueryClassification.Should().Be("general_model_knowledge");
        context.IntentType.Should().Be("explanation");
        context.RetrievalAllowed.Should().BeFalse();
        context.GroundingMode.Should().Be("non_grounded");
    }

    [Fact]
    public void TryApply_does_not_treat_self_profile_questions_as_uploaded_context_lookup()
    {
        var context = GeneralKnowledgeContext("WHAT DO YOU UNDERSTAND ABOUT ME");

        AskUploadedContextLookupPolicy.TryApply(context, out _).Should().BeFalse();

        context.QueryClassification.Should().Be("general_model_knowledge");
        context.IntentType.Should().Be("explanation");
        context.RetrievalAllowed.Should().BeFalse();
    }

    [Fact]
    public void Restore_returns_context_to_native_general_knowledge_route()
    {
        var context = GeneralKnowledgeContext("What is Polaris?");
        AskUploadedContextLookupPolicy.TryApply(context, out var snapshot).Should().BeTrue();

        AskUploadedContextLookupPolicy.Restore(context, snapshot);

        context.QueryClassification.Should().Be("general_model_knowledge");
        context.IntentType.Should().Be("explanation");
        context.RetrievalAllowed.Should().BeFalse();
        context.GroundingMode.Should().Be("non_grounded");
        context.RetrievalSkippedReason.Should().Be("general_knowledge_does_not_require_enterprise_retrieval");
        context.RetrievalDomainTerms.Should().BeEmpty();
    }

    private static AskWorkflowContext GeneralKnowledgeContext(string question)
    {
        return new AskWorkflowContext(
            "tenant-1",
            "user-1",
            question,
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "general_model_knowledge",
            QueryClassificationReason = "General knowledge question does not require enterprise source retrieval.",
            IntentType = "explanation",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            RetrievalSkippedReason = "general_knowledge_does_not_require_enterprise_retrieval",
            EvidenceRequired = false
        };
    }
}
