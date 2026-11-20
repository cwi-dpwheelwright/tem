using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Retrieval;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskDirectAnswerStepTests
{
    [Fact]
    public async Task ExecuteAsync_answers_simple_arithmetic_without_model_inference()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", "What is 2 + 2?", "enterprise", null, "observe", 3)
        {
            QueryClassification = "deterministic_reasoning",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            RetrievalSkippedReason = "deterministic_reasoning_does_not_require_sources"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Be("2 + 2 = 4.");
        context.SkipModelInference.Should().BeTrue();
        context.ModelName.Should().Be("deterministic-reasoner");
        context.RetrievalAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_answers_identity_from_system_truth_without_retrieval()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", "What is CWI?", "enterprise", null, "observe", 3)
        {
            QueryClassification = "identity_definition",
            RetrievalAllowed = false,
            GroundingMode = "system_truth",
            RetrievalSkippedReason = "system_truth_uses_governed_identity_pack"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Christian Wheelwright Intelligence");
        context.Answer.Should().Contain("private, governed, distributed AI system");
        context.SkipModelInference.Should().BeTrue();
        context.ModelName.Should().Be("governed-system-truth");
        context.RetrievalAllowed.Should().BeFalse();
        context.GroundingMode.Should().Be("system_truth");
    }

    [Fact]
    public async Task ExecuteAsync_clears_unrelated_evidence_for_direct_identity_answers()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", "What is CWI?", "enterprise", null, "observe", 3)
        {
            QueryClassification = "identity_definition",
            RetrievalAllowed = false,
            GroundingMode = "system_truth",
            MemoryResults = new[] { new GovernedMemoryResult { MemoryId = "mem-css", Text = "CSS context" } },
            SourceResults = new[] { new GovernedSourceResult { SourceId = "repo-css", ChunkId = "chunk-1", Text = "CSS context" } }
        };
        context.Retrieved.Add(new RetrievalResult("repo-css", "chunk-1", "CSS context", 0.99f));

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Christian Wheelwright Intelligence");
        context.Retrieved.Should().BeEmpty();
        context.GraphEvidence.Should().BeEmpty();
        context.MemoryResults.Should().BeEmpty();
        context.SourceResults.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_answers_generic_deployment_plan_without_source_retrieval()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", "Give me a deployment plan.", "enterprise", null, "observe", 3)
        {
            QueryClassification = "general_reasoning",
            IntentType = "operational",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            RetrievalSkippedReason = "missing_retrieval_intent_or_domain_context"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("generic deployment plan");
        context.Answer.Should().Contain("Run smoke tests");
        context.SkipModelInference.Should().BeTrue();
        context.ModelName.Should().Be("non-grounded-direct-reasoner");
        context.RetrievalAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_answers_cwi_memory_activation_from_system_truth()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", "Explain how CWI memory activation works.", "enterprise", null, "observe", 3)
        {
            QueryClassification = "cwi_memory_activation_explanation",
            IntentType = "explanation",
            RetrievalAllowed = false,
            GroundingMode = "system_truth",
            RetrievalSkippedReason = "system_truth_uses_governed_memory_architecture"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("governed bridge");
        context.Answer.Should().Contain("approved user context");
        context.Answer.Should().Contain("native CWI path");
        context.Answer.Should().NotContain("Cognitive Word Integration");
        context.SkipModelInference.Should().BeTrue();
        context.ModelName.Should().Be("governed-system-truth");
        context.GroundingMode.Should().Be("system_truth");
    }

    [Fact]
    public async Task ExecuteAsync_answers_generic_executive_demo_prompt_without_source_retrieval()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", "Give me an executive summary for the investor demo.", "enterprise", null, "observe", 3)
        {
            QueryClassification = "general_reasoning",
            IntentType = "decision_support",
            AudienceMode = "executive",
            TemplateUsed = "executive_summary",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            RetrievalSkippedReason = "missing_retrieval_intent_or_domain_context"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Impact:");
        context.Answer.Should().Contain("validated demo path");
        context.Answer.Should().Contain("Recommendation:");
        context.SkipModelInference.Should().BeTrue();
        context.ModelName.Should().Be("non-grounded-executive-reasoner");
        context.RetrievalAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_answers_troubleshooting_without_source_retrieval()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "My Docker container restarted but curl failed. What should I check next?",
            "enterprise",
            null,
            "observe",
            3)
        {
            QueryClassification = "troubleshooting",
            IntentType = "troubleshooting",
            TemplateUsed = "operator_troubleshooting",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            RetrievalSkippedReason = "troubleshooting_without_explicit_source_request",
            RetrievalDomainTerms = new[] { "docker", "container", "curl" }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Start here:");
        context.Answer.Should().Contain("docker ps");
        context.Answer.Should().Contain("curl -v");
        context.SkipModelInference.Should().BeTrue();
        context.ModelName.Should().Be("non-grounded-troubleshooting-reasoner");
        context.RetrievalAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_answers_casual_troubleshooting_without_identity_leak()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "yo this is broken",
            "enterprise",
            null,
            "observe",
            3)
        {
            QueryClassification = "troubleshooting",
            IntentType = "troubleshooting",
            TemplateUsed = "operator_troubleshooting",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            RetrievalSkippedReason = "troubleshooting_without_explicit_source_request"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Got you - something broke.");
        context.Answer.Should().Contain("Start here:");
        context.Answer.Should().NotContain("Christian Wheelwright Intelligence");
        context.SkipModelInference.Should().BeTrue();
        context.ModelName.Should().Be("non-grounded-troubleshooting-reasoner");
    }

    [Fact]
    public async Task ExecuteAsync_acknowledges_self_context_update_without_explaining_memory_back()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "My name is Demitrius Wheelwright and I am a Software Engineer",
            "personal",
            null,
            "observe",
            3)
        {
            QueryClassification = "self_context_update",
            IntentType = "memory_update",
            RetrievalAllowed = false,
            GroundingMode = "memory_update",
            RetrievalSkippedReason = "self_context_update_routes_to_memory"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Be("Got it, Demitrius Wheelwright. I will use Software Engineer as your work context.");
        context.Answer.Should().NotContain("available context");
        context.Answer.Should().NotContain("User stated durable context");
        context.Route.Should().Be("memory_update_acknowledgement");
        context.GroundingMode.Should().Be("memory_update");
        context.RetrievalAllowed.Should().BeFalse();
        context.SkipModelInference.Should().BeTrue();
        context.ModelName.Should().Be("governed-memory-update");
    }

    [Fact]
    public async Task ExecuteAsync_returns_complete_python_for_reverse_string_request()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "make me pyhton code to reverse a string",
            "enterprise",
            null,
            "observe",
            3)
        {
            QueryClassification = "code_generation",
            IntentType = "code_generation",
            TemplateUsed = "code_answer",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("def reverse_string");
        context.Answer.Should().Contain("return value[::-1]");
        context.Answer.Should().Contain("if __name__ == \"__main__\"");
        context.SkipModelInference.Should().BeTrue();
        context.ModelName.Should().Be("deterministic-code-template");
    }

    [Theory]
    [InlineData("make python code to sum numbers from 1 to 10", "total_sum = 0")]
    [InlineData("write a python factorial function", "def factorial")]
    [InlineData("make python code for list filtering", "even_numbers")]
    [InlineData("make python code with simple error handling", "except ValueError")]
    public async Task ExecuteAsync_returns_complete_python_for_common_basic_code_prompts(
        string question,
        string expectedSnippet)
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", question, "enterprise", null, "observe", 3)
        {
            QueryClassification = "code_generation",
            IntentType = "code_generation",
            TemplateUsed = "code_answer",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain(expectedSnippet);
        context.Answer.Should().Contain("```python");
        context.SkipModelInference.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_asks_for_text_when_summary_request_has_no_content()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "sumarize this for me in short words",
            "enterprise",
            null,
            "observe",
            3)
        {
            QueryClassification = "summarization",
            IntentType = "summarization",
            TemplateUsed = "summary_answer",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Paste the text");
        context.Answer.Should().NotContain("governed evidence");
        context.SkipModelInference.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_asks_clarification_for_ambiguous_action()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", "call john", "enterprise", null, "observe", 3)
        {
            QueryClassification = "clarification_required",
            IntentType = "needs_clarification",
            TemplateUsed = "clarification",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("one more detail");
        context.Route.Should().Be("clarification_handler");
        context.SkipModelInference.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_asks_send_recipient_clarification_without_treating_to_as_target()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", "send to jon", "enterprise", null, "observe", 3)
        {
            QueryClassification = "clarification_required",
            IntentType = "needs_clarification",
            TemplateUsed = "clarification",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("send to Jon");
        context.Answer.Should().NotContain("taking action on to jon");
        context.Answer.Should().NotContain("action on to Jon");
        context.Route.Should().Be("clarification_handler");
        context.ExecutionState.Should().Be("clarification_required");
    }

    [Fact]
    public async Task ExecuteAsync_asks_for_message_input_when_sendable_message_prompt_has_no_content()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", "I need a sendable message first, then one sentence explaining why it works.", "enterprise", null, "observe", 3)
        {
            QueryClassification = "clarification_required",
            IntentType = "needs_clarification",
            TemplateUsed = "clarification",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("What should the message be about?");
        context.Answer.Should().NotContain("report");
        context.Answer.Should().NotContain("[date]");
        context.Route.Should().Be("clarification_handler");
        context.ExecutionState.Should().Be("clarification_required");
    }

    [Fact]
    public async Task ExecuteAsync_answers_meta_send_it_prompt_with_single_targeted_clarification()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", "If I ask \"send it,\" what should you ask before helping?", "enterprise", null, "observe", 3)
        {
            QueryClassification = "general_model_knowledge",
            IntentType = "explanation",
            TemplateUsed = "general_answer",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Be("What should I send, and who should receive it?");
        context.Route.Should().Be("clarification_policy");
        context.SkipModelInference.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_gates_tool_intent_without_execution()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "whats teh wether tmrw do i need jaket",
            "enterprise",
            null,
            "observe",
            3)
        {
            QueryClassification = "tool_intent_request",
            IntentType = "weather.get",
            TemplateUsed = "tool_readiness",
            RetrievalAllowed = false,
            GroundingMode = "tool_intent",
            RetrievalSkippedReason = "mcp_execution_requires_p0_activation_gate"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("weather lookup");
        context.Answer.Should().Contain("MCP execution is still gated");
        context.Route.Should().Be("tool_intent_gated");
        context.SkipModelInference.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_uses_primary_question_for_executive_summary_after_failure_context()
    {
        var step = new AskDirectAnswerStep();
        var question = """
            Primary question:
            Summarize the impact of this system.

            The primary question is authoritative. Use the user-provided context below only when it directly supports the primary question.

            Conversation context from this chat:
            Turn 1
            User: yo this is broken
            Assistant: Start here with troubleshooting steps.
            """;
        var context = new AskWorkflowContext("tenant", "user", question, "enterprise", null, "observe", 3)
        {
            QueryClassification = "general_reasoning",
            IntentType = "decision_support",
            AudienceMode = "executive",
            TemplateUsed = "executive_summary",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            RetrievalSkippedReason = "missing_retrieval_intent_or_domain_context"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Impact:");
        context.Answer.Should().Contain("Recommendation:");
        context.Answer.Should().NotContain("Verification command:");
        context.SkipModelInference.Should().BeTrue();
        context.ModelName.Should().Be("non-grounded-executive-reasoner");
    }

    [Fact]
    public async Task ExecuteAsync_summarizes_user_provided_text_instead_of_echoing()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "Summarize this in short words: \"CWI is a governed orchestration layer that applies policy, evidence, memory, and traceability to model responses.\"",
            "enterprise",
            null,
            "observe",
            3)
        {
            QueryClassification = "summarization",
            IntentType = "summarization",
            TemplateUsed = "summary_answer",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("policy");
        context.Answer.Should().Contain("traceable proof");
        context.Answer.Should().NotContain("governed orchestration layer that applies");
    }

    [Fact]
    public async Task ExecuteAsync_blocks_current_info_when_web_is_explicitly_off()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "Web is off. Tell me the latest Houston Rockets news.",
            "enterprise",
            null,
            "observe",
            3)
        {
            QueryClassification = "current_events_web_disabled",
            IntentType = "current_events",
            TemplateUsed = "general_answer",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            RetrievalSkippedReason = "web_disabled_for_current_info"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Web retrieval is off");
        context.Answer.Should().NotContain("search provider");
        context.Route.Should().Be("current_info_web_disabled");
    }

    [Theory]
    [InlineData("Answer in one sentence: what should I do if I see a bank charge I do not recognize?", "official", "dispute")]
    [InlineData("I shared my phone number on a sketchy site - what should I do first?", "verification", "Block")]
    [InlineData("I clicked a weird link but did not enter any information - what should I do?", "entered nothing", "monitor")]
    [InlineData("Can I ignore an email that says my account closes today if it looks urgent?", "official", "email link")]
    public async Task ExecuteAsync_answers_everyday_security_questions_with_verification_steps(
        string question,
        string expectedFirst,
        string expectedSecond)
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", question, "enterprise", null, "observe", 3)
        {
            QueryClassification = "general_model_knowledge",
            IntentType = "explanation",
            TemplateUsed = "general_answer",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain(expectedFirst);
        context.Answer.Should().Contain(expectedSecond);
        context.Answer.Should().NotContain("change your phone number immediately");
        context.Route.Should().Be("everyday_safety_boundary");
        context.ModelName.Should().Be("everyday-safety-boundary");
        context.SkipModelInference.Should().BeTrue();
    }

    [Theory]
    [InlineData("I cannot find my keys and I need to leave in 15 minutes - what is the best search plan?", "Start with a 10-minute search", "trusted person")]
    [InlineData("I have 25 minutes before practice - should I eat, shower, or pack first?", "Pack first", "only shower if you truly need to")]
    [InlineData("I have homework, dinner, and laundry tonight - what order should I do them in?", "Start laundry first", "homework while the laundry runs")]
    [InlineData("I need to choose between going to sleep and finishing a late assignment - help me decide.", "minimum acceptable version", "message the teacher early")]
    [InlineData("If I ask for a daily briefing but there is no synced calendar or task data, what should the answer look like?", "cannot see your tasks", "paste the list")]
    public async Task ExecuteAsync_answers_everyday_practical_prompts_with_concrete_first_step(
        string question,
        string expectedFirst,
        string expectedSecond)
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", question, "enterprise", null, "observe", 3)
        {
            QueryClassification = "general_reasoning",
            IntentType = "practical_guidance",
            TemplateUsed = "practical_guidance",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain(expectedFirst);
        context.Answer.Should().Contain(expectedSecond);
        context.Answer.Should().NotContain("window");
        context.Route.Should().Be("everyday_practical_guidance");
        context.ModelName.Should().Be("everyday-practical-guidance");
        context.SkipModelInference.Should().BeTrue();
    }

    [Theory]
    [InlineData("Answer in one word: can I publish this private customer note?")]
    [InlineData("Can I share this private customer note with the team?")]
    [InlineData("Answer casually: can I expose this API token in a ticket?")]
    public async Task ExecuteAsync_keeps_privacy_safety_context_for_customer_notes(string question)
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", question, "enterprise", null, "observe", 3)
        {
            QueryClassification = "general_model_knowledge",
            IntentType = "explanation",
            TemplateUsed = "general_answer",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        var answer = context.Answer.Should().NotBeNull().And.Subject;
        answer.Should().Contain("No.");
        (answer.Contains("authorization", StringComparison.OrdinalIgnoreCase)
         || answer.Contains("secure secret handoff", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_clarifies_generic_execution_safety()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", "Is this safe to execute?", "enterprise", null, "observe", 3)
        {
            QueryClassification = "general_model_knowledge",
            IntentType = "explanation",
            TemplateUsed = "general_answer",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("specific action");
        context.Answer.Should().Contain("not approved");
        context.SkipModelInference.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_clarifies_destructive_delete_action_even_if_classifier_misses_it()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext("tenant", "user", "Delete the old file.", "enterprise", null, "observe", 3)
        {
            QueryClassification = "general_model_knowledge",
            IntentType = "explanation",
            TemplateUsed = "general_answer",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("exact item");
        context.Answer.Should().Contain("approval");
        context.Answer.Should().NotContain("right-click");
        context.Route.Should().Be("clarification_handler");
        context.ExecutionState.Should().Be("clarification_required");
    }

    [Fact]
    public async Task ExecuteAsync_renders_framework_comparison_table()
    {
        var step = new AskDirectAnswerStep();
        var context = new AskWorkflowContext(
            "tenant",
            "user",
            "Compare Memory Framework vs Presentation Framework in a table.",
            "enterprise",
            null,
            "observe",
            3)
        {
            QueryClassification = "general_reasoning",
            IntentType = "decision_support",
            TemplateUsed = "executive_summary",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("| Framework |");
        context.Answer.Should().Contain("Memory");
        context.Answer.Should().Contain("Presentation");
    }
}
