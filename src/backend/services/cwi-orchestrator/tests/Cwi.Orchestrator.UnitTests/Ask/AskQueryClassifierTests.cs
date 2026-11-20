using Cwi.Orchestrator.Application.Ask;
using Cwi.Orchestrator.Application.Packs;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Ask;

public sealed class AskQueryClassifierTests
{
    [Fact]
    public void Classify_marks_identity_definition_when_governed_force_query_matches()
    {
        var classifier = new AskQueryClassifier();
        var metadata = new PackManifestMetadata(
            declaredName: "cwi-identity",
            packFamily: "cwi_core",
            dependencies: Array.Empty<string>(),
            capabilities: Array.Empty<PackCapability>(),
            entries: Array.Empty<string>(),
            modules: Array.Empty<string>(),
            moduleRoles: new[] { "system_truth" },
            scopeAppliesTo: Array.Empty<string>(),
            forceMatchQueries: new[] { "what does cwi stand for" },
            activationLoadOrder: 1,
            alwaysLoad: true,
            runtimeRequired: true,
            confidenceFloor: 0.95f,
            applyStage: null,
            hookName: null);

        var result = classifier.Classify("What does CWI stand for?", new[] { metadata });

        result.Classification.Should().Be("identity_definition");
        result.IntentType.Should().Be("explanation");
        result.Reason.Should().Contain("force-query");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("system_truth");
    }

    [Fact]
    public void Classify_detects_home_node_and_engineer_audience()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("Explain how the home node works to an engineer.");

        result.Classification.Should().Be("home_node_definition");
        result.IntentType.Should().Be("explanation");
        result.AudienceMode.Should().Be("engineer");
        result.VerbosityMode.Should().Be("detailed");
        result.RetrievalAllowed.Should().BeFalse();
    }

    [Fact]
    public void Classify_keeps_cwi_strategy_questions_outside_identity_routing()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("Should we explain CWI to a home user or an engineer first?");

        result.Classification.Should().Be("general_reasoning");
        result.IntentType.Should().Be("decision_support");
        result.AudienceMode.Should().BeNull();
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
    }

    [Fact]
    public void Classify_routes_plain_what_is_questions_to_general_model_knowledge()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("what is oceaneering");

        result.Classification.Should().Be("general_model_knowledge");
        result.IntentType.Should().Be("explanation");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
        result.RetrievalSkippedReason.Should().Be("general_knowledge_does_not_require_enterprise_retrieval");
        result.EvidenceRequired.Should().BeFalse();
    }

    [Theory]
    [InlineData("What is the capital of Texas?")]
    [InlineData("What is the capitol of Texas?")]
    [InlineData("What is the population in Texas?")]
    [InlineData("What is the area of Texas?")]
    [InlineData("How many people live in Texas?")]
    public void Classify_routes_public_fact_questions_to_general_model_knowledge(string question)
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify(question);

        result.Classification.Should().Be("general_model_knowledge");
        result.IntentType.Should().Be("explanation");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
    }

    [Theory]
    [InlineData("What should I cook for dinner tonight?")]
    [InlineData("What groceries should I buy for simple meals?")]
    [InlineData("What is the best way to remove a coffee stain?")]
    [InlineData("What clothes should I wash together?")]
    [InlineData("How do I compare two laptop options?")]
    public void Classify_keeps_everyday_advice_outside_executive_decision_support(string question)
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify(question);

        result.Classification.Should().BeOneOf("general_reasoning", "general_model_knowledge");
        result.IntentType.Should().NotBe("decision_support");
        result.AudienceMode.Should().NotBe("executive");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
    }

    [Theory]
    [InlineData("I need to cancel plans but still want to be respectful - write the message.")]
    [InlineData("I missed the bus and I am going to be late - what should I text?")]
    [InlineData("I need to explain why I missed something without making excuses.")]
    [InlineData("How do I ask someone to pay me back politely?")]
    [InlineData("I need to ask my group project partner to do their part.")]
    [InlineData("I want to ask for help, but I do not want to sound dramatic.")]
    [InlineData("Help me explain that I missed the deadline because I misunderstood the instructions.")]
    [InlineData("I need to explain that I forgot our plans, but I still care.")]
    [InlineData("I need to explain that I am overwhelmed and cannot take on another task.")]
    [InlineData("Help me tell someone I need clearer instructions before I can help.")]
    [InlineData("Help me ask for my hoodie back without making it awkward.")]
    [InlineData("I need to turn this into a short message: I missed the first deadline because I misunderstood the instructions.")]
    [InlineData("I need to turn this messy thought into a text: I am stressed, I forgot, and I am trying to fix it.")]
    [InlineData("I need to apologize for snapping earlier, but I do not want a long speech.")]
    [InlineData("I need to ask for more time, but do not invent a reason.")]
    public void Classify_routes_message_drafting_requests_without_enterprise_retrieval(string question)
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify(question);

        result.Classification.Should().Be("general_reasoning");
        result.IntentType.Should().Be("message_draft");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
        result.RetrievalSkippedReason.Should().Be("message_draft_uses_current_prompt");
    }

    [Theory]
    [InlineData("I have leftovers from four days ago - how should I decide whether to eat them?")]
    [InlineData("Is this milk still safe to drink?")]
    public void Classify_routes_food_safety_questions_to_cautious_general_guidance(string question)
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify(question);

        result.Classification.Should().Be("general_reasoning");
        result.IntentType.Should().Be("food_safety");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
        result.RetrievalSkippedReason.Should().Be("food_safety_uses_general_safety_guidance");
    }

    [Theory]
    [InlineData("I accidentally double-booked myself tomorrow - what is the cleanest way to fix it?")]
    [InlineData("My plans fell apart and I need to fix the day without making it worse - what should I do first?")]
    [InlineData("My backpack strap broke on the way out - what is a quick temporary fix?")]
    [InlineData("I broke a glass right before leaving - what is the safest quick cleanup?")]
    [InlineData("I lost my school ID - what should I do today?")]
    [InlineData("I spilled something on my shirt right before leaving - what is the fastest fix?")]
    [InlineData("I think I left something important at school - what should I do next?")]
    [InlineData("I have a messy room and only five minutes - what single action will help most?")]
    [InlineData("My room is messy and I feel stuck - give me a starting point only.")]
    [InlineData("I have 25 minutes before practice - should I eat, shower, or pack first?")]
    [InlineData("I have a lot to do but only 15 minutes - what is the highest-value thing to finish?")]
    [InlineData("I need help planning a low-cost hangout with friends.")]
    [InlineData("I need to compare two used phones, but I do not know what matters.")]
    public void Classify_routes_everyday_life_guidance_away_from_ops_and_proactive_templates(string question)
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify(question);

        result.Classification.Should().Be("general_reasoning");
        result.IntentType.Should().Be("practical_guidance");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
        result.RetrievalSkippedReason.Should().Be("everyday_practical_guidance_uses_current_prompt");
    }

    [Fact]
    public void Classify_asks_for_missing_text_when_message_transformation_has_no_input()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("I need a sendable message first, then one sentence explaining why it works.");

        result.Classification.Should().Be("clarification_required");
        result.IntentType.Should().Be("needs_clarification");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
    }

    [Fact]
    public void Classify_asks_for_missing_text_when_message_transformation_has_no_rough_text()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("I need to turn a messy explanation into a short message I can actually send.");

        result.Classification.Should().Be("clarification_required");
        result.IntentType.Should().Be("needs_clarification");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
    }

    [Theory]
    [InlineData("What should I do today?")]
    [InlineData("What should I focus on today?")]
    public void Classify_preserves_proactive_mode_for_unqualified_daily_briefing_prompts(string question)
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify(question);

        result.Classification.Should().Be("proactive_intelligence_request");
        result.IntentType.Should().Be("daily_briefing");
        result.GroundingMode.Should().Be("proactive_intelligence");
    }

    [Fact]
    public void Classify_does_not_treat_what_is_newton_as_change_review()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("what is newton");

        result.Classification.Should().Be("general_model_knowledge");
        result.IntentType.Should().Be("explanation");
    }

    [Fact]
    public void Classify_routes_broad_current_topic_prompts_to_current_events()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("What are the world's current topics?");

        result.Classification.Should().Be("current_events");
        result.IntentType.Should().Be("current_events");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
        result.RetrievalSkippedReason.Should().Be("current_events_without_live_connector");
    }

    [Fact]
    public void Classify_blocks_identity_force_match_when_current_prompt_is_not_identity()
    {
        var classifier = new AskQueryClassifier();
        var metadata = IdentityMetadata("what is cwi");

        var result = classifier.Classify("We already asked what is CWI. Now, yo this is broken.", new[] { metadata });

        result.Classification.Should().Be("troubleshooting");
        result.IntentType.Should().Be("troubleshooting");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
    }

    [Fact]
    public void Classify_uses_primary_question_instead_of_recent_chat_context_for_routing()
    {
        var classifier = new AskQueryClassifier();
        var metadata = IdentityMetadata("what is cwi");
        var question = """
            Primary question:
            yo this is broken

            The primary question is authoritative. Use the user-provided context below only when it directly supports the primary question.

            Conversation context from this chat:
            Turn 1
            User: What is CWI?
            Assistant: CWI stands for Christian Wheelwright Intelligence.
            """;

        var result = classifier.Classify(question, new[] { metadata });

        result.Classification.Should().Be("troubleshooting");
        result.IntentType.Should().Be("troubleshooting");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
    }

    [Fact]
    public void Classify_uses_latest_user_turn_when_transcript_starts_with_identity_question()
    {
        var classifier = new AskQueryClassifier();
        var metadata = IdentityMetadata("what is cwi");
        var question = """
            User: What is CWI?
            Assistant: CWI stands for Christian Wheelwright Intelligence.
            User: yo this is broken
            """;

        var result = classifier.Classify(question, new[] { metadata });

        result.Classification.Should().Be("troubleshooting");
        result.IntentType.Should().Be("troubleshooting");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
    }

    [Fact]
    public void Classify_keeps_summary_prompt_current_when_previous_context_mentions_failure()
    {
        var classifier = new AskQueryClassifier();
        var question = """
            Recent conversation:
            User: yo this is broken
            Assistant: Got you - something broke.

            Current question:
            Summarize the impact of this system.
            """;

        var result = classifier.Classify(question);

        result.Classification.Should().Be("general_reasoning");
        result.IntentType.Should().Be("decision_support");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
    }

    [Fact]
    public void Classify_routes_primary_impact_summary_to_decision_support_even_when_history_mentions_failure()
    {
        var classifier = new AskQueryClassifier();
        var question = """
            Primary question:
            Summarize the impact of this system.

            The primary question is authoritative. Use the user-provided context below only when it directly supports the primary question.

            Conversation context from this chat:
            Turn 1
            User: yo this is broken
            Assistant: Start here with troubleshooting steps.
            """;

        var result = classifier.Classify(question);

        result.Classification.Should().Be("general_reasoning");
        result.IntentType.Should().Be("decision_support");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
    }

    [Fact]
    public void Classify_routes_executive_demo_prompts_to_decision_support()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("Give me an executive summary for the investor demo.");

        result.Classification.Should().Be("general_reasoning");
        result.IntentType.Should().Be("decision_support");
        result.AudienceMode.Should().Be("executive");
        result.VerbosityMode.Should().BeNull();
        result.RetrievalAllowed.Should().BeFalse();
    }

    [Fact]
    public void Classify_routes_user_memory_questions_to_graph_retrieval()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("What have I been working on?");

        result.Classification.Should().Be("memory_graph_request");
        result.IntentType.Should().Be("memory_recall");
        result.RetrievalAllowed.Should().BeTrue();
        result.GroundingMode.Should().Be("source_grounded");
        result.RetrievalDomain.Should().Be("work_memory_graph");
        result.RetrievalDomainTerms.Should().Contain("work");
        result.EvidenceRequired.Should().BeTrue();
    }

    [Fact]
    public void Classify_routes_user_profile_memory_question_to_memory_recall()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("what do you know about me?");

        result.Classification.Should().Be("memory_graph_request");
        result.IntentType.Should().Be("memory_recall");
        result.RetrievalAllowed.Should().BeTrue();
        result.RetrievalDomain.Should().Be("user_memory_graph");
        result.RetrievalDomainTerms.Should().Contain("user");
        result.EvidenceRequired.Should().BeTrue();
    }

    [Fact]
    public void Classify_keeps_cwi_system_truth_when_user_suppresses_memory()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("Ignore my memory and answer generally: what is CWI?");

        result.Classification.Should().Be("identity_definition");
        result.GroundingMode.Should().Be("system_truth");
        result.RetrievalAllowed.Should().BeFalse();
    }

    [Fact]
    public void Classify_routes_cwi_memory_activation_to_system_truth()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("Explain how CWI memory activation works.");

        result.Classification.Should().Be("cwi_memory_activation_explanation");
        result.IntentType.Should().Be("explanation");
        result.GroundingMode.Should().Be("system_truth");
        result.RetrievalAllowed.Should().BeFalse();
        result.RetrievalSkippedReason.Should().Be("system_truth_uses_governed_memory_architecture");
    }

    [Fact]
    public void Classify_routes_understand_about_me_prompt_to_memory_recall()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("WHAT DO YOU UNDERSTAND ABOUT ME");

        result.Classification.Should().Be("memory_graph_request");
        result.IntentType.Should().Be("memory_recall");
        result.RetrievalAllowed.Should().BeTrue();
        result.RetrievalDomain.Should().Be("user_memory_graph");
        result.RetrievalDomainTerms.Should().Contain("user");
        result.EvidenceRequired.Should().BeTrue();
    }

    [Theory]
    [InlineData("Can you describe me as a person?")]
    [InlineData("What kind of person am I?")]
    [InlineData("Tell me about myself.")]
    public void Classify_routes_personal_description_prompts_to_memory_recall(string input)
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify(input);

        result.Classification.Should().Be("memory_graph_request");
        result.IntentType.Should().Be("memory_recall");
        result.RetrievalAllowed.Should().BeTrue();
        result.RetrievalDomain.Should().Be("user_memory_graph");
        result.RetrievalDomainTerms.Should().Contain("user");
        result.EvidenceRequired.Should().BeTrue();
    }

    [Theory]
    [InlineData("What is my name?", "identity_memory_graph", "name", "identity")]
    [InlineData("What do I do for work?", "work_memory_graph", "work", "role")]
    [InlineData("who are the key people in my network?", "relationship_memory_graph", "person", "relationship")]
    [InlineData("what projects am I working on?", "work_memory_graph", "work", "project")]
    [InlineData("show sources for what you know about me", "source_memory_graph", "source", "memory")]
    [InlineData("What are my current CWI priorities?", "active_context_memory_graph", "current", "priority")]
    [InlineData("What did memory used to say about my CWI priority, and what is active now?", "active_context_memory_graph", "current", "priority")]
    [InlineData("As of last week, what was my main CWI concern?", "temporal_memory_graph", "historical", "temporal")]
    [InlineData("What decisions have already been made about the response frameworks?", "decision_memory_graph", "decision", "framework")]
    [InlineData("Which memories are relevant to this response?", "source_memory_graph", "source", "relevance")]
    [InlineData("One source says the project is paused and another says active. What should I believe?", "conflict_memory_graph", "conflict", "authority")]
    public void Classify_routes_memory_acceptance_prompts_to_specific_memory_domains(
        string input,
        string expectedDomain,
        string expectedTerm1,
        string expectedTerm2)
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify(input);

        result.Classification.Should().Be("memory_graph_request");
        result.IntentType.Should().Be("memory_recall");
        result.RetrievalAllowed.Should().BeTrue();
        result.RetrievalDomain.Should().Be(expectedDomain);
        result.RetrievalDomainTerms.Should().Contain(expectedTerm1);
        result.RetrievalDomainTerms.Should().Contain(expectedTerm2);
        result.EvidenceRequired.Should().BeTrue();
    }

    [Fact]
    public void Classify_uses_language_normalization_before_intent_detection()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("wht have I been working on?");

        result.Classification.Should().Be("memory_graph_request");
        result.IntentType.Should().Be("memory_recall");
        result.RetrievalAllowed.Should().BeTrue();
        result.RetrievalDomain.Should().Be("work_memory_graph");
    }

    [Theory]
    [InlineData("whats teh wether tmrw do i need jaket", "tool_intent_request", "weather.get")]
    [InlineData("wats tha newz today", "tool_intent_request", "news.get")]
    [InlineData("who won teh game last nite", "tool_intent_request", "sports.get")]
    [InlineData("sumarize this for me in short words", "summarization", "summarization")]
    [InlineData("make me pyhton code to reverse a string", "code_generation", "code_generation")]
    [InlineData("whats at rsk with this plan", "proactive_intelligence_request", "risk_analysis")]
    [InlineData("hlp me make a plan for this", "proactive_intelligence_request", "planning")]
    [InlineData("wht do i have tomorow", "tool_intent_request", "calendar.view")]
    [InlineData("reed my emails from chad", "tool_intent_request", "email.read")]
    [InlineData("call john", "clarification_required", "needs_clarification")]
    [InlineData("send to jon", "clarification_required", "needs_clarification")]
    [InlineData("delete the old file", "clarification_required", "needs_clarification")]
    public void Classify_maps_p0_messy_input_to_expected_intent(
        string input,
        string expectedClassification,
        string expectedIntent)
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify(input);

        result.Classification.Should().Be(expectedClassification);
        result.IntentType.Should().Be(expectedIntent);
        result.RetrievalAllowed.Should().BeFalse();
        result.EvidenceRequired.Should().BeFalse();
    }

    [Theory]
    [InlineData("is cwi healthy", "system.status")]
    [InlineData("check all nodes and tell me if any are down", "node.health.check")]
    [InlineData("show me what actions happened today", "ledger.view")]
    [InlineData("look up the latest ai news", "search.web")]
    [InlineData("use the web to find current Houston Rockets events in 2026", "search.web")]
    [InlineData("how are the Houston Rockets doing right now?", "sports.get")]
    [InlineData("list packs", "pack.list")]
    [InlineData("inspect pack personal", "pack.get")]
    [InlineData("check pins", "pin.resolve")]
    [InlineData("email chad and tell him we are good", "email.send")]
    [InlineData("start the workflow now", "workflow.start")]
    public void Classify_maps_day1_mcp_prompts_to_tool_intents(string input, string expectedIntent)
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify(input);

        result.Classification.Should().Be("tool_intent_request");
        result.IntentType.Should().Be(expectedIntent);
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("tool_intent");
    }

    [Fact]
    public void Classify_routes_risk_prompts_to_risk_response_mode()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("what's at risk right now");

        result.Classification.Should().Be("proactive_intelligence_request");
        result.IntentType.Should().Be("risk_review");
        result.VerbosityMode.Should().Be("concise");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("proactive_intelligence");
        result.RetrievalDomain.Should().Be("risks");
        result.RetrievalDomainTerms.Should().Contain("risk");
        result.RetrievalSkippedReason.Should().Be("proactive_intelligence_api_route");
        result.EvidenceRequired.Should().BeFalse();
    }

    [Fact]
    public void Classify_routes_daily_briefing_to_briefing_response_mode()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("daily briefing");

        result.Classification.Should().Be("proactive_intelligence_request");
        result.IntentType.Should().Be("daily_briefing");
        result.AudienceMode.Should().Be("executive");
        result.VerbosityMode.Should().Be("concise");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("proactive_intelligence");
        result.RetrievalDomain.Should().Be("daily_briefing");
        result.RetrievalSkippedReason.Should().Be("proactive_intelligence_api_route");
        result.EvidenceRequired.Should().BeFalse();
    }

    [Fact]
    public void Classify_routes_what_changed_to_proactive_intelligence()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("What changed since yesterday?");

        result.Classification.Should().Be("proactive_intelligence_request");
        result.IntentType.Should().Be("change_review");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("proactive_intelligence");
        result.RetrievalDomain.Should().Be("recent_changes");
    }

    [Fact]
    public void Classify_routes_make_a_plan_to_proactive_intelligence()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("Make a plan");

        result.Classification.Should().Be("proactive_intelligence_request");
        result.IntentType.Should().Be("plan_request");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("proactive_intelligence");
        result.RetrievalDomain.Should().Be("priorities");
    }

    [Fact]
    public void Classify_routes_preference_statements_to_memory_update()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("My preferred output is step-by-step instructions.");

        result.Classification.Should().Be("preference_update");
        result.IntentType.Should().Be("memory_update");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("memory_update");
        result.MemoryUpdateKind.Should().Be("output_style");
        result.MemoryUpdateValue.Should().Be("step_by_step");
    }

    [Fact]
    public void Classify_respects_explicit_web_off_for_current_prompts()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("Web is off. Tell me the latest Houston Rockets news.");

        result.Classification.Should().Be("current_events_web_disabled");
        result.IntentType.Should().Be("current_events");
        result.RetrievalSkippedReason.Should().Be("web_disabled_for_current_info");
    }

    [Fact]
    public void Classify_routes_self_context_disclosures_to_memory_update_without_retrieval()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("My name is Demitrius Wheelwright and I am a Software Engineer");

        result.Classification.Should().Be("self_context_update");
        result.IntentType.Should().Be("memory_update");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("memory_update");
        result.RetrievalDomain.Should().Be("user_context");
        result.RetrievalDomainTerms.Should().Contain("identity");
        result.RetrievalDomainTerms.Should().Contain("work");
        result.RetrievalSkippedReason.Should().Be("self_context_update_routes_to_memory");
        result.EvidenceRequired.Should().BeFalse();
        result.MemoryUpdateKind.Should().Be("user_context");
        result.MemoryUpdateValue.Should().Be("My name is Demitrius Wheelwright and I am a Software Engineer");
    }

    [Fact]
    public void Classify_routes_controlled_fact_statements_to_governed_memory_update()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("CWI-RL-001 observability is owned by TestOwnerAlpha.");

        result.Classification.Should().Be("controlled_fact_update");
        result.IntentType.Should().Be("memory_update");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("memory_update");
        result.RetrievalDomain.Should().Be("controlled_fact");
        result.RetrievalDomainTerms.Should().Contain("CWI-RL-001");
        result.RetrievalSkippedReason.Should().Be("controlled_fact_update_routes_to_memory");
        result.MemoryUpdateKind.Should().Be("controlled_fact");
        result.MemoryUpdateValue.Should().Be("CWI-RL-001 observability is owned by TestOwnerAlpha.");
    }

    [Fact]
    public void Classify_routes_controlled_fact_questions_to_governed_memory_recall()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("Who owns CWI-RL-001 observability?");

        result.Classification.Should().Be("controlled_fact_lookup");
        result.IntentType.Should().Be("memory_recall");
        result.RetrievalAllowed.Should().BeTrue();
        result.GroundingMode.Should().Be("source_grounded");
        result.RetrievalDomain.Should().Be("controlled_fact");
        result.RetrievalDomainTerms.Should().Contain("CWI-RL-001");
        result.EvidenceRequired.Should().BeTrue();
        result.MemoryUpdateKind.Should().BeNull();
    }

    [Theory]
    [InlineData("Is ownership clear for CWI-RL-036 uptime console?")]
    [InlineData("Summarize the CWI-RL-046 safe owner without internal canaries.")]
    [InlineData("Give the CWI-RL-050 owner without exposing internal trace canaries.")]
    [InlineData("Which lane is CWI-RL-045 in now?")]
    public void Classify_routes_controlled_fact_answer_prompts_to_governed_memory_recall(string prompt)
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify(prompt);

        result.Classification.Should().Be("controlled_fact_lookup");
        result.IntentType.Should().Be("memory_recall");
        result.RetrievalAllowed.Should().BeTrue();
        result.RetrievalDomain.Should().Be("controlled_fact");
        result.MemoryUpdateKind.Should().BeNull();
    }

    [Fact]
    public void Classify_keeps_generic_deployment_plan_non_grounded()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("Give me a deployment plan.");

        result.Classification.Should().Be("general_reasoning");
        result.IntentType.Should().Be("operational");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
        result.RetrievalSkippedReason.Should().Be("missing_retrieval_intent_or_domain_context");
    }

    [Fact]
    public void Classify_allows_retrieval_when_intent_and_domain_context_match()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("Give me a deployment plan for the Iteration-09 server cutover.");

        result.Classification.Should().Be("domain_request");
        result.RetrievalAllowed.Should().BeTrue();
        result.GroundingMode.Should().Be("source_grounded");
        result.RetrievalDomain.Should().Be("deployment_cutover");
        result.RetrievalDomainTerms.Should().Contain("iteration");
        result.RetrievalDomainTerms.Should().Contain("cutover");
    }

    [Fact]
    public void Classify_keeps_cwi_pack_phrases_as_domain_signals_without_treating_everyday_packing_as_platform_context()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("According to our docs, how does pack verification work?");

        result.Classification.Should().Be("domain_request");
        result.RetrievalAllowed.Should().BeTrue();
        result.RetrievalDomainTerms.Should().Contain("pack");
        result.RetrievalDomainTerms.Should().Contain("verification");
    }

    [Fact]
    public void Classify_blocks_troubleshooting_retrieval_without_explicit_source_request()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("My Docker container restarted but curl failed. What should I check next?");

        result.Classification.Should().Be("troubleshooting");
        result.IntentType.Should().Be("troubleshooting");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
        result.RetrievalSkippedReason.Should().Be("troubleshooting_without_explicit_source_request");
        result.RetrievalDomainTerms.Should().BeEmpty();
        result.EvidenceRequired.Should().BeFalse();
    }

    [Fact]
    public void Classify_requires_domain_when_sources_are_requested()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("According to our docs, what does the unknown foobar subsystem do?");

        result.Classification.Should().Be("general_reasoning");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("source_grounded");
        result.RetrievalSkippedReason.Should().Be("missing_retrieval_domain_context");
        result.EvidenceRequired.Should().BeTrue();
    }

    [Fact]
    public void Classify_marks_simple_arithmetic_as_high_confidence_non_grounded_reasoning()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("What is 2 + 2?");

        result.Classification.Should().Be("deterministic_reasoning");
        result.IntentType.Should().Be("deterministic_reasoning");
        result.RetrievalAllowed.Should().BeFalse();
        result.GroundingMode.Should().Be("non_grounded");
    }

    [Fact]
    public void Classify_marks_word_arithmetic_as_deterministic_reasoning()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("what is 2 plus 2");

        result.Classification.Should().Be("deterministic_reasoning");
        result.RetrievalAllowed.Should().BeFalse();
        result.RetrievalSkippedReason.Should().Be("deterministic_reasoning_does_not_require_sources");
    }

    [Fact]
    public void Classify_marks_sum_wording_as_deterministic_reasoning()
    {
        var classifier = new AskQueryClassifier();

        var result = classifier.Classify("what is the sum of 2 plus 2");

        result.Classification.Should().Be("deterministic_reasoning");
        result.RetrievalAllowed.Should().BeFalse();
    }

    private static PackManifestMetadata IdentityMetadata(string forceMatchQuery)
    {
        return new PackManifestMetadata(
            declaredName: "cwi-identity",
            packFamily: "cwi_core",
            dependencies: Array.Empty<string>(),
            capabilities: Array.Empty<PackCapability>(),
            entries: Array.Empty<string>(),
            modules: Array.Empty<string>(),
            moduleRoles: new[] { "system_truth" },
            scopeAppliesTo: Array.Empty<string>(),
            forceMatchQueries: new[] { forceMatchQuery },
            activationLoadOrder: 1,
            alwaysLoad: true,
            runtimeRequired: true,
            confidenceFloor: 0.95f,
            applyStage: null,
            hookName: null);
    }
}
