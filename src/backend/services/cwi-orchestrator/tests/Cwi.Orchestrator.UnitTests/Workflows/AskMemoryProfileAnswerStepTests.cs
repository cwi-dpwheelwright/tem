#nullable enable
using Cwi.Common;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskMemoryProfileAnswerStepTests
{
    [Fact]
    public async Task ExecuteAsync_renders_profile_answer_from_activated_memory()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "what do you know about me?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "mem-cwi-work",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory work_career project_memory",
                    Text = "Christian is building CWI as a governed local-first agentic AI system with MCP and Teacher Node packs.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.94f,
                    Confidence = 0.92f,
                    ContentSha256 = "sha-work",
                    ProvenanceId = "prov-import-1"
                },
                new GovernedMemoryResult
                {
                    MemoryId = "mem-collab",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory relationships relationship_memory",
                    Text = "Demitrius owns orchestrator and MCP wiring while Chad owns Grafana and Smartsheet governance dashboards.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.91f,
                    Confidence = 0.90f,
                    ContentSha256 = "sha-people",
                    ProvenanceId = "prov-import-1"
                }
            }
        };
        var step = new AskMemoryProfileAnswerStep();

        var result = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.SkipModelInference.Should().BeTrue();
        context.Route.Should().Be("activated_memory_retrieval");
        context.TemplateUsed.Should().Be("memory_profile");
        context.Answer.Should().Contain("Here is what I understand");
        context.Answer.Should().Contain("CWI");
        context.Answer.Should().Contain("Demitrius");
        context.Answer.Should().NotContain("activated user memory");
        context.Answer.Should().NotContain("Sources:");
        context.Answer.Should().NotContain("prov-import-1");
    }

    [Fact]
    public async Task ExecuteAsync_uses_fallback_context_when_profile_categories_do_not_match()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "WHAT DO YOU UNDERSTAND ABOUT ME",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "mem-general-signal",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory general note",
                    Text = "User prefers responses that avoid backend implementation language and provide the useful answer first.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.90f,
                    Confidence = 0.86f,
                    ContentSha256 = "sha-general",
                    ProvenanceId = "prov-general"
                }
            },
            MemoryConflictSummaries = new[]
            {
                new MemoryConflictSummary
                {
                    ConflictId = "conflict-platform",
                    Slot = "projects",
                    Summary = "Unresolved memory conflict in projects: Polaris exists to transform raw, ambiguous, heterogeneous, sensitive, or high-value user-submitted data into decision-grade intelligence."
                }
            }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("Relevant Context");
        context.Answer.Should().Contain("avoid backend implementation language");
        context.Answer.Should().NotContain("activated user memory");
        context.Answer.Should().NotContain("Sources:");
        context.Answer.Should().NotContain("mem-general-signal");
        context.Answer.Should().NotContain("prov-general");
    }

    [Fact]
    public async Task ExecuteAsync_filters_chat_export_application_metadata_from_profile_answer()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "what do you know about me?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "mem-technical-export-state",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory general event_memory",
                    Text = "\"oai/apps/hasSeenProjectsUpdateNuxTooltip\": \"2025-06-14T20:09:39.333338+00:00\"",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.94f,
                    Confidence = 0.86f,
                    ContentSha256 = "sha-technical",
                    ProvenanceId = "prov-import-1"
                },
                new GovernedMemoryResult
                {
                    MemoryId = "mem-cwi-work",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory work_career project_memory",
                    Text = "User is building CWI memory activation with governed context retrieval.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.91f,
                    Confidence = 0.90f,
                    ContentSha256 = "sha-work",
                    ProvenanceId = "prov-import-1"
                }
            }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("CWI memory activation");
        context.Answer.Should().NotContain("oai/apps/");
        context.Answer.Should().NotContain("hasSeenProjectsUpdateNuxTooltip");
    }

    [Fact]
    public async Task ExecuteAsync_filters_imported_image_file_metadata_from_profile_answer()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "WHAT DO YOU UNDERSTAND ABOUT ME",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "mem-image-import-path",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory general event_memory",
                    Text = "Image imported into account chat history: archive.zip/user-qBcOIeCAtMqf3N31GjL0Tq8e/file_00000000d2586.png.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.94f,
                    Confidence = 0.86f,
                    ContentSha256 = "sha-image-path",
                    ProvenanceId = "prov-import-image"
                }
            }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("I don't have any stored memory about you yet");
        context.Answer.Should().NotContain("Image imported into account chat history");
        context.Answer.Should().NotContain("file_000000");
        context.Answer.Should().NotContain("prov-import-image");
    }

    [Fact]
    public async Task ExecuteAsync_normalizes_escaped_line_breaks_in_memory_answer()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "WHAT DO YOU UNDERSTAND ABOUT ME",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "mem-work-review",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory work_career project_memory",
                    Text = "User runs weekly production reviews with supervisors.\\nPurpose of reviews: understand production shortfalls.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.94f,
                    Confidence = 0.90f,
                    ContentSha256 = "sha-work-review",
                    ProvenanceId = "prov-import-1"
                }
            }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Route.Should().Be("activated_memory_retrieval");
        context.Answer.Should().Contain("weekly production reviews");
        context.Answer.Should().NotContain("\\n");
        context.Answer.Should().NotStartWith("UNDERSTAND is");
    }

    [Fact]
    public async Task ExecuteAsync_renders_source_list_for_memory_provenance_query()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "show sources for what you know about me",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "source_memory_graph",
            RetrievalDomainTerms = new[] { "source", "memory" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "mem-source",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory work_career project_memory",
                    Text = "CWI memory source test.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.94f,
                    Confidence = 0.88f,
                    ContentSha256 = "sha-source",
                    ProvenanceId = "chat-export-1"
                }
            }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.TemplateUsed.Should().Be("memory_sources");
        context.Answer.Should().Contain("Sources I used for this answer");
        context.Answer.Should().Contain("mem-source");
        context.Answer.Should().Contain("chat-export-1");
    }

    [Fact]
    public async Task ExecuteAsync_renders_uploaded_context_lookup_from_user_memory()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "Can you describe Polaris, the platform developed by Astrion9?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "uploaded_context_lookup",
            IntentType = "context_lookup",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "uploaded_user_context",
            RetrievalDomainTerms = new[] { "polaris", "astrion9" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "mem-polaris",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory uploaded_context project_memory",
                    Text = "Polaris exists to transform raw, ambiguous, heterogeneous, sensitive, or high-value user-submitted data into decision-grade intelligence.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.94f,
                    Confidence = 0.91f,
                    ContentSha256 = "sha-polaris",
                    ProvenanceId = "prov-polaris-upload"
                }
            },
            SourceResults = new[]
            {
                new GovernedSourceResult
                {
                    SourceId = "polaris-upload",
                    ChunkId = "chunk-1",
                    SourceType = "uploaded_source",
                    Text = """
                        Owner: Astrion9 Technologies
                        Purpose: Convert the current Polaris mission into a build-ready platform charter that incorporates the four Polaris frameworks: Origin, Compass, Veritas, and Zenith.
                        Polaris is the user-facing intelligence platform and execution fabric for four framework families.
                        Origin - the data-foundation and readiness framework.
                        Compass - the evidence, structure, signal, and enrichment framework.
                        Veritas - the truth, evidence-quality, inference, and audit framework.
                        Zenith - the forecasting, causal reasoning, and optimization framework.
                        Polaris may profile, enrich, synthesize, detect, map, infer, forecast, simulate, optimize, recommend, and explain.
                        Polaris must not silently mutate source truth, invent certainty, hide uncertainty, bypass policy, collapse proposed outputs into authoritative outputs, or publish unsupported conclusions.
                        """,
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.89f,
                    ContentSha256 = "sha-source"
                }
            }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.SkipModelInference.Should().BeTrue();
        context.Route.Should().Be("uploaded_context_memory_retrieval");
        context.TemplateUsed.Should().Be("uploaded_context_lookup");
        context.Answer.Should().Contain("Polaris is a governed intelligence platform from Astrion9 Technologies");
        context.Answer.Should().Contain("decision-grade intelligence");
        context.Answer.Should().Contain("Origin");
        context.Answer.Should().Contain("Compass");
        context.Answer.Should().Contain("Veritas");
        context.Answer.Should().Contain("Zenith");
        context.Answer.Should().Contain("must not silently mutate source truth");
        context.Answer.Should().NotContain("activated uploaded context");
        context.Answer.Should().NotContain("your uploaded context");
        context.Answer.Should().NotContain("prov-polaris-upload");
        context.Answer.Should().NotContain("source:polaris-upload#chunk-1");
        context.Answer.Should().NotContain("Unresolved context conflicts");
        context.Answer.Should().NotContain("fictional");
        context.Answer.Should().NotContain("North Star");
    }

    [Fact]
    public async Task ExecuteAsync_falls_back_to_general_knowledge_when_uploaded_context_has_no_evidence()
    {
        var nextCalled = false;
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "What is Oceaneering?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "uploaded_context_lookup",
            IntentType = "context_lookup",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "uploaded_user_context",
            RetrievalDomainTerms = new[] { "oceaneering" }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.SkipModelInference.Should().BeFalse();
        context.QueryClassification.Should().Be("general_model_knowledge");
        context.IntentType.Should().Be("explanation");
        context.RetrievalAllowed.Should().BeFalse();
        context.GroundingMode.Should().Be("non_grounded");
        context.Answer.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ignores_recent_conversation_summary_for_uploaded_context_lookup()
    {
        var nextCalled = false;
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "What is the capital of Texas?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "uploaded_context_lookup",
            IntentType = "context_lookup",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "uploaded_user_context",
            RetrievalDomainTerms = new[] { "capital", "texas" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "sum-recent",
                    MemoryScope = "conversation",
                    SourceType = "conversation_summary",
                    Text = "Recent conversation summary: I own Tektonic. | What is the capital of Texas? | What does CWI stand for?",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.95f,
                    Confidence = 0.78f,
                    ContentSha256 = "sha-summary",
                    ProvenanceId = "trace-summary"
                }
            }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () =>
            {
                nextCalled = true;
                return Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto()));
            },
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        nextCalled.Should().BeTrue();
        context.SkipModelInference.Should().BeFalse();
        context.QueryClassification.Should().Be("general_model_knowledge");
        context.IntentType.Should().Be("explanation");
        context.RetrievalAllowed.Should().BeFalse();
        context.GroundingMode.Should().Be("non_grounded");
        context.MemoryResults.Should().BeEmpty();
        context.Answer.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_renders_grounded_person_description_from_memory()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "Can you describe me as a person?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "mem-work-style",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory work_career project_memory current_work",
                    Text = "Christian is building CWI as a governed local-first agentic AI system and is focused on response frameworks.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.94f,
                    Confidence = 0.92f,
                    ContentSha256 = "sha-work",
                    ProvenanceId = "prov-import-1"
                },
                new GovernedMemoryResult
                {
                    MemoryId = "mem-preference",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory home_personal preference_memory response_preference",
                    Text = "Christian prefers concise paragraphs with grouped source pills.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.91f,
                    Confidence = 0.90f,
                    ContentSha256 = "sha-pref",
                    ProvenanceId = "prov-import-1"
                },
                new GovernedMemoryResult
                {
                    MemoryId = "mem-open-loop",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory work_career open_loop_memory",
                    Text = "OPEN-CEDAR-441: verify that imported memory shapes the answer without generic fallback.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.89f,
                    Confidence = 0.88f,
                    ContentSha256 = "sha-loop",
                    ProvenanceId = "prov-import-1"
                },
                new GovernedMemoryResult
                {
                    MemoryId = "mem-relationship",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory relationships relationship_memory",
                    Text = "Demitrius owns orchestrator validation and Chad owns dashboard reporting.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.87f,
                    Confidence = 0.87f,
                    ContentSha256 = "sha-people",
                    ProvenanceId = "prov-import-1"
                },
                new GovernedMemoryResult
                {
                    MemoryId = "mem-home-marker",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory home_personal preference_memory home_life",
                    Text = "For home life, my personal focus marker is HOME-CEDAR-118.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.86f,
                    Confidence = 0.86f,
                    ContentSha256 = "sha-home",
                    ProvenanceId = "prov-import-1"
                },
                new GovernedMemoryResult
                {
                    MemoryId = "mem-prior-bad-answer",
                    MemoryScope = "activated_user_memory",
                    SourceType = "conversation_summary",
                    Text = "Prior turn: user asked \"Can you describe me as a person?\"; CWI answered \"I will use general reasoning and common characteristics that apply broadly.\"",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.99f,
                    Confidence = 0.80f,
                    ContentSha256 = "sha-prior",
                    ProvenanceId = "prov-chat-1"
                }
            }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Route.Should().Be("activated_memory_retrieval");
        context.TemplateUsed.Should().Be("memory_person_description");
        context.Answer.Should().Contain("Based on what I understand so far, I would describe you this way");
        context.Answer.Should().Contain("Systems-minded and builder-oriented");
        context.Answer.Should().Contain("Evidence-driven and quality-focused");
        context.Answer.Should().Contain("Direct and practical");
        context.Answer.Should().Contain("Collaborative, but ownership-aware");
        context.Answer.Should().Contain("Persistent about open loops");
        context.Answer.Should().Contain("Grounding: Demitrius owns orchestrator validation and Chad owns dashboard reporting");
        context.Answer.Should().Contain("concise paragraphs");
        context.Answer.Should().Contain("grounded in what you have shared");
        context.Answer.Should().NotContain("Sources:");
        context.Answer.Should().NotContain("Grounding: For home life");
        context.Answer.Should().NotContain("Current focus:");
        context.Answer.Should().NotContain("Working style / standards:");
        context.Answer.Should().NotContain("typical traits");
        context.Answer.Should().NotContain("general standpoint");
        context.Answer.Should().NotContain("Prior turn");
        context.Answer.Should().NotContain("common characteristics");
    }

    [Fact]
    public async Task ExecuteAsync_answers_name_question_from_active_user_memory()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "What is my name?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "identity_memory_graph",
            RetrievalDomainTerms = new[] { "name", "identity" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "mem-user-name",
                    MemoryScope = "user",
                    SourceType = "identity",
                    Text = "User stated durable context: My name is Demitrius Wheelwright. I am 6'3 and I am a software engineer.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.90f,
                    Confidence = 0.82f,
                    ContentSha256 = "sha-name",
                    ProvenanceId = "trace-self-disclosure"
                }
            }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.SkipModelInference.Should().BeTrue();
        context.Route.Should().Be("activated_memory_retrieval");
        context.Answer.Should().Be("Your name is Demitrius Wheelwright.");
        context.Answer.Should().NotContain("I don't have access");
        context.Answer.Should().NotContain("personal information");
    }

    [Fact]
    public async Task ExecuteAsync_answers_work_role_question_from_active_user_memory()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "What do I do for work?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "work_memory_graph",
            RetrievalDomainTerms = new[] { "work", "role", "job", "profession" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "mem-user-role",
                    MemoryScope = "user",
                    SourceType = "work_role",
                    Text = "User stated durable context: My name is Demitrius Wheelwright and I am a Software Engineer",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.90f,
                    Confidence = 0.82f,
                    ContentSha256 = "sha-role",
                    ProvenanceId = "trace-work-role"
                }
            }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.SkipModelInference.Should().BeTrue();
        context.Route.Should().Be("activated_memory_retrieval");
        context.TemplateUsed.Should().Be("memory_work_role");
        context.Answer.Should().Be("You work as a Software Engineer.");
        context.Answer.Should().NotContain("job market");
        context.Answer.Should().NotContain("career paths");
    }

    [Fact]
    public async Task ExecuteAsync_renders_broad_profile_from_durable_self_context()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "What do you know about me?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "mem-user-self-context",
                    MemoryScope = "user",
                    SourceType = "identity",
                    Text = "My name is Christian and I am in operations.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.90f,
                    Confidence = 0.82f,
                    ContentSha256 = "sha-self",
                    ProvenanceId = "trace-self"
                },
                new GovernedMemoryResult
                {
                    MemoryId = "mem-platform-charter",
                    MemoryScope = "activated_user_memory",
                    SourceType = "activated_user_memory work_career project_memory work_life",
                    Text = "Polaris may profile, enrich, synthesize, detect, map, infer, forecast, simulate, optimize, recommend, and explain. Polaris must not silently mutate source truth.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.94f,
                    Confidence = 0.86f,
                    ContentSha256 = "sha-platform",
                    ProvenanceId = "trace-platform"
                }
            }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Route.Should().Be("activated_memory_retrieval");
        context.Answer.Should().Contain("Christian");
        context.Answer.Should().Contain("operations");
        context.Answer.Should().Contain("Your name is Christian and you are in operations");
        context.Answer.Should().NotContain("Polaris may profile");
        context.Answer.Should().NotContain("source truth");
        context.Answer.Should().NotContain("Polaris exists to transform");
        context.Answer.Should().NotContain("Unresolved context conflicts");
        context.Answer.Should().NotContain("I don't have any stored memory");
        context.Answer.Should().NotContain("activated user memory");
        context.Answer.Should().NotContain("User stated durable context");
    }

    [Fact]
    public async Task ExecuteAsync_returns_empty_memory_state_without_evidence_boilerplate()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "what do you know about me?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            EvidenceRequired = true
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.SkipModelInference.Should().BeTrue();
        context.Route.Should().Be("activated_memory_state");
        context.Grounded.Should().BeFalse();
        context.EvidenceRequired.Should().BeFalse();
        context.RetrievalSkippedReason.Should().Be("memory_retrieval_returned_no_usable_memory");
        context.Answer.Should().Contain("I don't have any stored memory about you yet");
        context.Answer.Should().NotContain("not have enough governed evidence");
    }

    [Fact]
    public async Task ExecuteAsync_controlled_fact_lookup_uses_exact_entity_durable_facts_only()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "Use current evidence: which lane is CWI-RL-045 in now?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "controlled_fact_lookup",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "controlled_fact",
            RetrievalDomainTerms = new[] { "CWI-RL-045", "lane", "current" },
            EvidenceRequired = true,
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "sum-rl-045",
                    MemoryScope = "conversation",
                    SourceType = "conversation_summary",
                    Text = "Recent conversation summary: CWI-RL-045 validation lane is LaneQuartz. CWI-RL-045 validation lane changed to LaneTopaz.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.99f,
                    Confidence = 0.82f,
                    ContentSha256 = "sha-summary",
                    ProvenanceId = "trace-summary"
                },
                new GovernedMemoryResult
                {
                    MemoryId = "mem-old",
                    MemoryScope = "user",
                    SourceType = "controlled_fact",
                    Text = "CWI-RL-045 validation lane is LaneQuartz.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.94f,
                    Confidence = 0.82f,
                    ContentSha256 = "sha-old",
                    ProvenanceId = "trace-old"
                },
                new GovernedMemoryResult
                {
                    MemoryId = "mem-new",
                    MemoryScope = "user",
                    SourceType = "controlled_fact",
                    Text = "CWI-RL-045 validation lane changed to LaneTopaz.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.94f,
                    Confidence = 0.82f,
                    ContentSha256 = "sha-new",
                    ProvenanceId = "trace-new"
                },
                new GovernedMemoryResult
                {
                    MemoryId = "mem-other",
                    MemoryScope = "user",
                    SourceType = "controlled_fact",
                    Text = "CWI-RL-044 active dependency changed to DependencyOrchid.",
                    TenantId = "tenant-1",
                    UserId = "user-1",
                    RelevanceScore = 0.94f,
                    Confidence = 0.82f,
                    ContentSha256 = "sha-other",
                    ProvenanceId = "trace-other"
                }
            }
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Answer.Should().Contain("CWI-RL-045");
        context.Answer.Should().Contain("LaneTopaz");
        context.Answer.Should().NotContain("Recent conversation summary");
        context.Answer.Should().NotContain("DependencyOrchid");
        context.Answer.Should().NotContain("Current learned fact for Use");
    }

    [Fact]
    public async Task ExecuteAsync_returns_restricted_memory_state_when_access_is_not_allowed()
    {
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "what do you know about me?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = false,
            GroundingMode = "source_grounded",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            EvidenceRequired = true
        };

        var result = await new AskMemoryProfileAnswerStep().ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        context.Route.Should().Be("activated_memory_state");
        context.EvidenceRequired.Should().BeFalse();
        context.RetrievalSkippedReason.Should().Be("memory_access_not_permitted");
        context.Answer.Should().Contain("not permitted to access it");
    }
}
#nullable disable
