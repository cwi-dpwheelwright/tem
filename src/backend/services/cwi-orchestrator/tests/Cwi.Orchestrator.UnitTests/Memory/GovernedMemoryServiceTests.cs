#nullable enable
using Cwi.Orchestrator.Application.Documents;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Domain.Entities;
using Cwi.Orchestrator.Domain.Repositories;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Memory;

public sealed class GovernedMemoryServiceTests
{
    [Fact]
    public async Task UpdateAfterAnswerAsync_promotes_self_disclosure_to_user_memory()
    {
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        var service = new GovernedMemoryService(
            new InMemoryAskHistoryRepository(),
            memoryRepository,
            new InMemorySourceMemoryRepository(),
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "My name is Demitrius Wheelwright. I am 6'3 and I am a software engineer.",
            "personal",
            null,
            "observe",
            5)
        {
            Answer = "Got it.",
            TraceId = "trace-self-disclosure"
        };

        var update = await service.UpdateAfterAnswerAsync(context, CancellationToken.None);

        update.Created.Should().ContainSingle(decision =>
            decision.Reason == "durable_user_or_project_context_detected");
        memoryRepository.Memories.Should().ContainSingle(memory =>
            memory.UserId == "user-1"
            && memory.MemoryScope == "user"
            && memory.MemoryType == "identity"
            && memory.MemoryText.Contains("Demitrius Wheelwright", StringComparison.Ordinal)
            && memory.ProvenanceId == "trace-self-disclosure");
        memoryRepository.Memories.Should().NotContain(memory =>
            memory.MemoryText.Contains("User stated durable context", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UpdateAfterAnswerAsync_stores_controlled_fact_without_private_canary_clause()
    {
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        var service = new GovernedMemoryService(
            new InMemoryAskHistoryRepository(),
            memoryRepository,
            new InMemorySourceMemoryRepository(),
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "CWI-RL-046 safe summary owner is TestOwnerNacre. Internal privacy canary PRIVATE-CANARY-RL-046 must not be repeated.",
            "personal",
            null,
            "observe",
            5)
        {
            Answer = "Controlled fact recorded in governed memory.",
            QueryClassification = "controlled_fact_update",
            MemoryUpdateKind = "controlled_fact",
            MemoryUpdateValue = "CWI-RL-046 safe summary owner is TestOwnerNacre. Internal privacy canary PRIVATE-CANARY-RL-046 must not be repeated.",
            TraceId = "trace-controlled-fact"
        };

        var update = await service.UpdateAfterAnswerAsync(context, CancellationToken.None);

        update.Created.Should().ContainSingle(decision =>
            decision.Reason == "controlled_fact_memory_update_detected");
        memoryRepository.Memories.Should().ContainSingle(memory =>
            memory.MemoryType == "controlled_fact"
            && memory.MemoryText.Contains("CWI-RL-046", StringComparison.Ordinal)
            && memory.MemoryText.Contains("TestOwnerNacre", StringComparison.Ordinal)
            && !memory.MemoryText.Contains("PRIVATE-CANARY", StringComparison.OrdinalIgnoreCase)
            && !memory.MemoryText.Contains("internal privacy canary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RetrieveAsync_boosts_controlled_fact_memory_for_matching_entity_lookup()
    {
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        await memoryRepository.UpsertMemoryAsync(new GovernedMemoryRecord
        {
            Id = Guid.NewGuid(),
            MemoryId = "mem-controlled-1",
            TenantId = "tenant-1",
            UserId = "user-1",
            MemoryScope = "user",
            MemoryType = "controlled_fact",
            MemoryText = "CWI-RL-001 observability is owned by TestOwnerAlpha.",
            Confidence = 0.82f,
            SensitivityLevel = "normal",
            RetentionPolicy = "durable",
            AccessScope = "user",
            LocalOnly = false,
            SyncAllowed = true,
            ContentSha256 = "hash",
            ProvenanceId = "trace-controlled-1",
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        }, CancellationToken.None);
        var service = new GovernedMemoryService(
            new InMemoryAskHistoryRepository(),
            memoryRepository,
            new InMemorySourceMemoryRepository(),
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "Who owns CWI-RL-001 observability?",
            "personal",
            null,
            "observe",
            5)
        {
            QueryClassification = "controlled_fact_lookup",
            IntentType = "memory_recall",
            RetrievalDomain = "controlled_fact",
            RetrievalDomainTerms = new[] { "CWI-RL-001", "observability" },
            RetrievalAllowed = true,
            GroundingMode = "source_grounded"
        };

        var result = await service.RetrieveAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.MemoryResults.Should().ContainSingle(memory =>
            memory.MemoryId == "mem-controlled-1"
            && memory.RelevanceScore >= 0.94f
            && memory.Text.Contains("TestOwnerAlpha", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RetrieveAsync_returns_relevant_memory_and_source_chunks_only()
    {
        var history = new InMemoryAskHistoryRepository();
        await history.AddAsync(new AskHistoryRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "user-1",
            Question = "My preferred style for handoffs is detailed step-by-step with exact paths.",
            Answer = "I will remember that preference for future handoffs.",
            TraceId = "prov-memory",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        }, CancellationToken.None);
        await history.AddAsync(new AskHistoryRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "user-1",
            Question = "What is the weather?",
            Answer = "It is sunny.",
            TraceId = "prov-unrelated",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-4)
        }, CancellationToken.None);
        await history.AddAsync(new AskHistoryRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "user-1",
            Question = "Remember this password",
            Answer = "password: secret-value",
            TraceId = "prov-secret",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-3)
        }, CancellationToken.None);

        var documents = new DocumentStore();
        documents.RecordIngest(new IngestDocumentRequestDto
        {
            TenantId = "tenant-1",
            UserId = "user-1",
            Document = new IngestDocumentPayloadDto
            {
                DocId = "src-handoff",
                Title = "Handoff notes",
                Content = "Relevant handoff source chunk with exact file paths.",
                Source = "unit-test"
            }
        });
        documents.RecordIngest(new IngestDocumentRequestDto
        {
            TenantId = "tenant-1",
            UserId = "other-user",
            Document = new IngestDocumentPayloadDto
            {
                DocId = "src-private-other",
                Title = "Other user notes",
                Content = "Relevant handoff source chunk owned by another user.",
                Source = "unit-test"
            }
        });

        var memoryRepository = new InMemoryGovernedMemoryRepository();
        var sourceRepository = new InMemorySourceMemoryRepository();
        var service = new GovernedMemoryService(
            history,
            memoryRepository,
            sourceRepository,
            documents,
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant-1", "user-1", "Create a handoff for Demitrius.", "pack", null, "observe", 5)
        {
            IntentType = "handoff"
        };

        var result = await service.RetrieveAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.MemoryResults.Should().ContainSingle(memory =>
            memory.Text.Contains("preferred style", StringComparison.OrdinalIgnoreCase)
            && memory.ProvenanceId == "prov-memory");
        result.Data.MemoryResults.Should().NotContain(memory =>
            memory.Text.Contains("password", StringComparison.OrdinalIgnoreCase)
            || memory.Text.Contains("weather", StringComparison.OrdinalIgnoreCase));
        result.Data.SourceResults.Should().ContainSingle(source => source.SourceId == "src-handoff");
        result.Data.SourceResults.Should().NotContain(source => source.SourceId == "src-private-other");
    }

    [Fact]
    public async Task RetrieveAsync_keeps_recent_self_disclosure_active_for_name_questions()
    {
        var history = new InMemoryAskHistoryRepository();
        await history.AddAsync(new AskHistoryRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "user-1",
            Question = "My name is Demitrius Wheelwright. I am 6'3 and I am a software engineer.",
            Answer = "Got it.",
            TraceId = "trace-self-disclosure",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
        }, CancellationToken.None);
        var service = new GovernedMemoryService(
            history,
            new InMemoryGovernedMemoryRepository(),
            new InMemorySourceMemoryRepository(),
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant-1", "user-1", "What is my name?", "pack", null, "observe", 5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalDomain = "identity_memory_graph",
            RetrievalDomainTerms = new[] { "name", "identity" },
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            EvidenceRequired = true
        };

        var result = await service.RetrieveAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.MemoryResults.Should().ContainSingle(memory =>
            memory.MemoryScope == "conversation"
            && memory.Text.Contains("Demitrius Wheelwright", StringComparison.Ordinal)
            && memory.RelevanceScore >= 0.80f
            && memory.Confidence >= 0.80f);
        context.MemoryFrameworkDecisions.Should().Contain(decision =>
            decision.Outcome == "active"
            && decision.Reason == "selected_for_active_context");
    }

    [Fact]
    public async Task RetrieveAsync_keeps_recent_self_disclosure_active_for_work_role_questions()
    {
        var history = new InMemoryAskHistoryRepository();
        await history.AddAsync(new AskHistoryRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            UserId = "user-1",
            Question = "My name is Demitrius Wheelwright and I am a Software Engineer",
            Answer = "Hello Demitrius Wheelwright.",
            TraceId = "trace-work-role",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
        }, CancellationToken.None);
        var service = new GovernedMemoryService(
            history,
            new InMemoryGovernedMemoryRepository(),
            new InMemorySourceMemoryRepository(),
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant-1", "user-1", "What do I do for work?", "pack", null, "observe", 5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalDomain = "work_memory_graph",
            RetrievalDomainTerms = new[] { "work", "role", "job", "profession" },
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            EvidenceRequired = true
        };

        var result = await service.RetrieveAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.MemoryResults.Should().ContainSingle(memory =>
            memory.MemoryScope == "conversation"
            && memory.Text.Contains("Software Engineer", StringComparison.Ordinal)
            && memory.RelevanceScore >= 0.80f
            && memory.Confidence >= 0.80f);
        context.MemoryFrameworkDecisions.Should().Contain(decision =>
            decision.Outcome == "active"
            && decision.Reason == "selected_for_active_context");
    }

    [Fact]
    public async Task RetrieveAsync_includes_durable_self_context_for_broad_profile_questions()
    {
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        await memoryRepository.UpsertMemoryAsync(new GovernedMemoryRecord
        {
            Id = Guid.NewGuid(),
            MemoryId = "mem-user-self-context",
            TenantId = "tenant-1",
            UserId = "user-1",
            MemoryScope = "user",
            MemoryType = "identity",
            MemoryText = "My name is Christian and I am in operations.",
            Confidence = 0.82f,
            SensitivityLevel = "normal",
            RetentionPolicy = "durable",
            AccessScope = "user",
            ContentSha256 = "sha-self-context",
            ProvenanceId = "trace-self-context",
            CreatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        }, CancellationToken.None);

        var service = new GovernedMemoryService(
            new InMemoryAskHistoryRepository(),
            memoryRepository,
            new InMemorySourceMemoryRepository(),
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext("tenant-1", "user-1", "What do you know about me?", "pack", null, "observe", 5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            EvidenceRequired = true
        };

        var result = await service.RetrieveAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.MemoryResults.Should().ContainSingle(memory =>
            memory.MemoryId == "mem-user-self-context"
            && memory.Text.Contains("Christian", StringComparison.Ordinal)
            && memory.Text.Contains("operations", StringComparison.Ordinal)
            && memory.RelevanceScore >= 0.95f);
        context.MemoryFrameworkDecisions.Should().Contain(decision =>
            decision.MemoryId == "mem-user-self-context"
            && decision.Outcome == "active");
    }

    [Fact]
    public async Task RetrieveAsync_loads_activated_imported_memory_for_profile_questions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cwi-activated-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var capsulesPath = Path.Combine(directory, "memory_capsules.jsonl");
            await File.WriteAllTextAsync(
                capsulesPath,
                """
                {"memory_id":"mem-cwi-work","tenant_id":"tenant-1","user_id":"user-1","memory_type":"project_memory","domain":"work_career","content":"Christian is building CWI as a governed local-first agentic AI system with MCP, Teacher Node packs, ledgers, and provenance.","source_type":"imported_chat_history","source_id":"chat-export-1","source_sha256":"sha-work","confidence":0.92,"sensitivity_level":"private","provenance_id":"prov-import-1"}
                {"memory_id":"mem-collab","tenant_id":"tenant-1","user_id":"user-1","memory_type":"relationship_memory","domain":"relationships","content":"Demitrius owns orchestrator and MCP wiring while Chad owns Grafana and Smartsheet governance dashboards.","source_type":"imported_chat_history","source_id":"chat-export-1","source_sha256":"sha-people","confidence":0.90,"sensitivity_level":"private","provenance_id":"prov-import-1"}
                {"memory_id":"mem-other-user","tenant_id":"tenant-1","user_id":"other-user","memory_type":"project_memory","domain":"work_career","content":"Other user private memory should not be returned.","source_type":"imported_chat_history","source_id":"chat-export-2","source_sha256":"sha-other","confidence":0.95,"sensitivity_level":"private","provenance_id":"prov-import-2"}
                {"memory_id":"mem-unscoped","tenant_id":"tenant-1","memory_type":"project_memory","domain":"work_career","content":"Unscoped imported memory should not be treated as shared memory.","source_type":"imported_chat_history","source_id":"chat-export-4","source_sha256":"sha-unscoped","confidence":0.95,"sensitivity_level":"private","provenance_id":"prov-import-4"}
                {"memory_id":"mem-secret","tenant_id":"tenant-1","user_id":"user-1","memory_type":"preference_memory","domain":"work_career","content":"The access token is secret and should not be surfaced.","source_type":"imported_chat_history","source_id":"chat-export-3","source_sha256":"sha-secret","confidence":0.95,"sensitivity_level":"private","provenance_id":"prov-import-3"}
                """,
                CancellationToken.None);
            await File.WriteAllTextAsync(
                Path.Combine(directory, "user_profile.json"),
                """{"summary":"Unmanifested structured artifact should not be loaded for every user."}""",
                CancellationToken.None);

            var provider = new ActivatedMemoryArtifactProvider(new ActivatedMemoryOptions
            {
                ActivatedDirectory = directory,
                ActivationLedgerPath = Path.Combine(directory, "memory_activation_events.jsonl")
            });
            var service = new GovernedMemoryService(
                new InMemoryAskHistoryRepository(),
                new InMemoryGovernedMemoryRepository(),
                new InMemorySourceMemoryRepository(),
                new DocumentStore(),
                JourneyTestHelpers.CreateEmitter(),
                provider);
            var context = new AskWorkflowContext(
                "tenant-1",
                "user-1",
                "what do you know about me?",
                "pack",
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
                EvidenceRequired = true,
                TraceId = "trace-memory"
            };

            var result = await service.RetrieveAsync(context, CancellationToken.None);

            result.IsSuccess.Should().BeTrue();
            result.Data!.MemoryResults.Should().Contain(memory => memory.MemoryId == "mem-cwi-work");
            result.Data.MemoryResults.Should().Contain(memory => memory.Text.Contains("Demitrius", StringComparison.OrdinalIgnoreCase));
            result.Data.MemoryResults.Should().NotContain(memory => memory.MemoryId == "mem-other-user");
            result.Data.MemoryResults.Should().NotContain(memory => memory.MemoryId == "mem-unscoped");
            result.Data.MemoryResults.Should().NotContain(memory =>
                memory.Text.Contains("Unmanifested structured artifact", StringComparison.OrdinalIgnoreCase));
            result.Data.MemoryResults.Should().NotContain(memory => memory.Text.Contains("access token", StringComparison.OrdinalIgnoreCase));
            File.Exists(Path.Combine(directory, "memory_activation_events.jsonl")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RetrieveAsync_scores_imported_governed_memory_for_profile_questions_without_keyword_overlap()
    {
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        var now = DateTimeOffset.UtcNow;
        await memoryRepository.UpsertMemoryAsync(new GovernedMemoryRecord
        {
            Id = Guid.NewGuid(),
            MemoryId = "mem-work",
            TenantId = "tenant-1",
            UserId = "user-1",
            MemoryScope = "activated_user_memory",
            MemoryType = "activated_user_memory work_career project_memory current_work",
            MemoryText = "Christian is building CWI as a governed local-first agentic AI system with MCP and Teacher Node packs.",
            Confidence = 0.91f,
            SensitivityLevel = "private_work",
            RetentionPolicy = "user_owned",
            AccessScope = "user",
            ContentSha256 = "sha-work",
            ProvenanceId = "chat-export-1",
            CreatedUtc = now,
            UpdatedUtc = now
        }, CancellationToken.None);
        await memoryRepository.UpsertMemoryAsync(new GovernedMemoryRecord
        {
            Id = Guid.NewGuid(),
            MemoryId = "mem-people",
            TenantId = "tenant-1",
            UserId = "user-1",
            MemoryScope = "activated_user_memory",
            MemoryType = "activated_user_memory relationships relationship_memory responsibility",
            MemoryText = "Demitrius owns orchestrator deployment and Chad owns dashboard follow-up.",
            Confidence = 0.89f,
            SensitivityLevel = "private_work",
            RetentionPolicy = "user_owned",
            AccessScope = "user",
            ContentSha256 = "sha-people",
            ProvenanceId = "chat-export-1",
            CreatedUtc = now,
            UpdatedUtc = now
        }, CancellationToken.None);
        await memoryRepository.UpsertMemoryAsync(new GovernedMemoryRecord
        {
            Id = Guid.NewGuid(),
            MemoryId = "mem-other-user",
            TenantId = "tenant-1",
            UserId = "other-user",
            MemoryScope = "activated_user_memory",
            MemoryType = "activated_user_memory work_career project_memory current_work",
            MemoryText = "Other user memory must not be returned.",
            Confidence = 0.98f,
            SensitivityLevel = "private_work",
            RetentionPolicy = "user_owned",
            AccessScope = "user",
            ContentSha256 = "sha-other",
            ProvenanceId = "chat-export-2",
            CreatedUtc = now,
            UpdatedUtc = now
        }, CancellationToken.None);
        var service = new GovernedMemoryService(
            new InMemoryAskHistoryRepository(),
            memoryRepository,
            new InMemorySourceMemoryRepository(),
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "what do you know about me?",
            "pack",
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

        var result = await service.RetrieveAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.MemoryResults.Should().Contain(memory => memory.MemoryId == "mem-work");
        result.Data.MemoryResults.Should().Contain(memory => memory.MemoryId == "mem-people");
        result.Data.MemoryResults.Should().NotContain(memory => memory.MemoryId == "mem-other-user");
    }

    [Fact]
    public async Task RetrieveAsync_does_not_treat_null_user_activated_memory_as_shared()
    {
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        var now = DateTimeOffset.UtcNow;
        await memoryRepository.UpsertMemoryAsync(new GovernedMemoryRecord
        {
            Id = Guid.NewGuid(),
            MemoryId = "mem-unscoped-import",
            TenantId = "tenant-1",
            UserId = null,
            MemoryScope = "activated_user_memory",
            MemoryType = "activated_user_memory work_career project_memory",
            MemoryText = "Imported chat history says the user is building a private CWI memory bridge.",
            Confidence = 0.92f,
            SensitivityLevel = "private_work",
            RetentionPolicy = "user_owned",
            AccessScope = "user",
            ContentSha256 = "sha-unscoped",
            ProvenanceId = "chat-export-unscoped",
            CreatedUtc = now,
            UpdatedUtc = now
        }, CancellationToken.None);
        await memoryRepository.UpsertMemoryAsync(new GovernedMemoryRecord
        {
            Id = Guid.NewGuid(),
            MemoryId = "mem-tenant-policy",
            TenantId = "tenant-1",
            UserId = null,
            MemoryScope = "tenant",
            MemoryType = "policy",
            MemoryText = "Tenant memory says handoffs should include acceptance criteria.",
            Confidence = 0.85f,
            SensitivityLevel = "normal",
            RetentionPolicy = "durable",
            AccessScope = "tenant",
            ContentSha256 = "sha-tenant",
            ProvenanceId = "tenant-policy",
            CreatedUtc = now,
            UpdatedUtc = now
        }, CancellationToken.None);
        var service = new GovernedMemoryService(
            new InMemoryAskHistoryRepository(),
            memoryRepository,
            new InMemorySourceMemoryRepository(),
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "what do you know about handoffs acceptance criteria and my private CWI memory bridge?",
            "pack",
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

        var result = await service.RetrieveAsync(context, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.MemoryResults.Should().NotContain(memory => memory.MemoryId == "mem-unscoped-import");
        result.Data.MemoryResults.Should().Contain(memory => memory.MemoryId == "mem-tenant-policy");
    }

    [Fact]
    public void BuildPostAnswerUpdate_marks_durable_context_and_blocks_secrets()
    {
        var service = new GovernedMemoryService(
            new InMemoryAskHistoryRepository(),
            new InMemoryGovernedMemoryRepository(),
            new InMemorySourceMemoryRepository(),
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var durableContext = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "My preferred style for handoffs is detailed step-by-step with exact paths.",
            "pack",
            null,
            "observe",
            3)
        {
            TraceId = "prov-answer",
            Answer = "Understood."
        };
        var secretContext = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "Remember this API key.",
            "pack",
            null,
            "observe",
            3)
        {
            TraceId = "prov-secret",
            Answer = "api key: secret"
        };

        var durableUpdate = service.BuildPostAnswerUpdate(durableContext);
        var secretUpdate = service.BuildPostAnswerUpdate(secretContext);

        durableUpdate.Created.Should().ContainSingle(decision =>
            decision.MemoryScope == "user"
            && decision.Reason == "durable_user_or_project_context_detected");
        durableUpdate.ProvenanceId.Should().Be("prov-answer");
        secretUpdate.Created.Should().BeEmpty();
        secretUpdate.Ignored.Should().ContainSingle(decision => decision.Reason == "secret_or_sensitive_content_not_stored");
        secretUpdate.ProvenanceId.Should().Be("prov-secret");
    }

    [Fact]
    public async Task UpdateAfterAnswerAsync_persists_approved_memory()
    {
        var memoryRepository = new InMemoryGovernedMemoryRepository();
        var service = new GovernedMemoryService(
            new InMemoryAskHistoryRepository(),
            memoryRepository,
            new InMemorySourceMemoryRepository(),
            new DocumentStore(),
            JourneyTestHelpers.CreateEmitter());
        var context = new AskWorkflowContext(
            "tenant-1",
            "user-1",
            "My preferred style for handoffs is detailed step-by-step with exact paths.",
            "pack",
            null,
            "observe",
            3)
        {
            TraceId = "prov-answer",
            Answer = "Understood."
        };

        var update = await service.UpdateAfterAnswerAsync(context, CancellationToken.None);

        update.Created.Should().ContainSingle();
        memoryRepository.Memories.Should().ContainSingle(memory =>
            memory.TenantId == "tenant-1"
            && memory.UserId == "user-1"
            && memory.MemoryScope == "user"
            && memory.ProvenanceId == "prov-answer");
    }

    private sealed class InMemoryAskHistoryRepository : IAskHistoryRepository
    {
        private readonly List<AskHistoryRecord> _records = new();

        public Task AddAsync(AskHistoryRecord record, CancellationToken cancellationToken)
        {
            _records.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AskHistoryRecord>> ListAsync(
            string tenantId,
            string userId,
            int limit,
            DateTimeOffset? createdAfter,
            CancellationToken cancellationToken)
        {
            var records = _records
                .Where(record => string.Equals(record.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
                .Where(record => string.Equals(record.UserId, userId, StringComparison.OrdinalIgnoreCase))
                .Where(record => createdAfter is null || record.CreatedUtc >= createdAfter.Value)
                .OrderByDescending(record => record.CreatedUtc)
                .Take(limit)
                .ToArray();

            return Task.FromResult<IReadOnlyList<AskHistoryRecord>>(records);
        }

        public Task<int> DeleteOlderThanAsync(
            string tenantId,
            string userId,
            DateTimeOffset cutoff,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task<int> DeleteAllAsync(string tenantId, string userId, CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }

        public Task<int> TrimToLatestAsync(
            string tenantId,
            string userId,
            int maxEntries,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(0);
        }
    }

    private sealed class InMemoryGovernedMemoryRepository : IGovernedMemoryRepository
    {
        public List<GovernedMemoryRecord> Memories { get; } = new();
        public List<ConversationSummaryRecord> Summaries { get; } = new();

        public Task UpsertMemoryAsync(GovernedMemoryRecord record, CancellationToken cancellationToken)
        {
            Memories.RemoveAll(memory => memory.TenantId == record.TenantId && memory.MemoryId == record.MemoryId);
            Memories.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<GovernedMemoryRecord>> ListMemoryCandidatesAsync(
            string tenantId,
            string userId,
            string? nodeId,
            int limit,
            CancellationToken cancellationToken)
        {
            var records = Memories
                .Where(memory => memory.TenantId == tenantId)
                .Where(memory =>
                    string.Equals(memory.UserId, userId, StringComparison.OrdinalIgnoreCase)
                    || (memory.UserId is null
                        && (string.Equals(memory.MemoryScope, "tenant", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(memory.AccessScope, "tenant", StringComparison.OrdinalIgnoreCase))))
                .Take(limit)
                .ToArray();

            return Task.FromResult<IReadOnlyList<GovernedMemoryRecord>>(records);
        }

        public Task UpsertConversationSummaryAsync(ConversationSummaryRecord record, CancellationToken cancellationToken)
        {
            Summaries.RemoveAll(summary =>
                summary.TenantId == record.TenantId
                && summary.UserId == record.UserId
                && summary.SummaryId == record.SummaryId);
            Summaries.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ConversationSummaryRecord>> ListConversationSummariesAsync(
            string tenantId,
            string userId,
            string conversationId,
            int limit,
            CancellationToken cancellationToken)
        {
            var records = Summaries
                .Where(summary =>
                    summary.TenantId == tenantId
                    && summary.UserId == userId
                    && summary.ConversationId == conversationId)
                .Take(limit)
                .ToArray();

            return Task.FromResult<IReadOnlyList<ConversationSummaryRecord>>(records);
        }
    }

    private sealed class InMemorySourceMemoryRepository : ISourceMemoryRepository
    {
        public List<SourceMemoryRecord> Sources { get; } = new();
        public List<SourceChunkRecord> Chunks { get; } = new();

        public Task UpsertSourceAsync(
            SourceMemoryRecord source,
            IReadOnlyList<SourceChunkRecord> chunks,
            CancellationToken cancellationToken)
        {
            Sources.RemoveAll(existing => existing.TenantId == source.TenantId && existing.SourceId == source.SourceId);
            Sources.Add(source);
            Chunks.RemoveAll(chunk => chunk.TenantId == source.TenantId && chunk.SourceId == source.SourceId);
            Chunks.AddRange(chunks);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SourceChunkRecord>> ListChunkCandidatesAsync(
            string tenantId,
            string userId,
            string? nodeId,
            int limit,
            CancellationToken cancellationToken)
        {
            var records = Chunks
                .Where(chunk => chunk.TenantId == tenantId)
                .Where(chunk => string.Equals(chunk.UserId, userId, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToArray();

            return Task.FromResult<IReadOnlyList<SourceChunkRecord>>(records);
        }

        public Task<IReadOnlyList<SourceMemoryRecord>> ListSourcesAsync(
            string tenantId,
            string? userId,
            string? sourceType,
            int limit,
            CancellationToken cancellationToken)
        {
            var sources = Sources
                .Where(source => source.TenantId == tenantId)
                .Where(source => string.IsNullOrWhiteSpace(userId)
                    ? source.UserId is null
                    : string.Equals(source.UserId, userId, StringComparison.OrdinalIgnoreCase))
                .Where(source => string.IsNullOrWhiteSpace(sourceType) || source.SourceType == sourceType)
                .Take(limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<SourceMemoryRecord>>(sources);
        }

        public Task<SourceMemoryRecord?> GetSourceAsync(
            string tenantId,
            string sourceId,
            string? userId,
            CancellationToken cancellationToken)
        {
            var source = Sources.FirstOrDefault(source =>
                source.TenantId == tenantId
                && source.SourceId == sourceId
                && (string.IsNullOrWhiteSpace(userId)
                    ? source.UserId is null
                    : string.Equals(source.UserId, userId, StringComparison.OrdinalIgnoreCase)));
            return Task.FromResult(source);
        }

        public Task<IReadOnlyList<SourceChunkRecord>> ListChunksForSourceAsync(
            string tenantId,
            string sourceId,
            string? userId,
            int limit,
            CancellationToken cancellationToken)
        {
            var chunks = Chunks
                .Where(chunk => chunk.TenantId == tenantId && chunk.SourceId == sourceId)
                .Where(chunk => string.IsNullOrWhiteSpace(userId)
                    ? chunk.UserId is null
                    : string.Equals(chunk.UserId, userId, StringComparison.OrdinalIgnoreCase))
                .Take(limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<SourceChunkRecord>>(chunks);
        }
    }
}
#nullable disable
