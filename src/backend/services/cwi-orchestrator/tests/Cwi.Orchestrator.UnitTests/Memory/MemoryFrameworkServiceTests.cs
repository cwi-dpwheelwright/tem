#nullable enable
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Workflows;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Memory;

public sealed class MemoryFrameworkServiceTests
{
    [Fact]
    public void BuildActiveContext_suppresses_duplicates_and_keeps_strongest_candidate()
    {
        var service = new MemoryFrameworkService();
        var context = CreateContext("what do you know about me?");
        var weaker = Memory("mem-weaker", "User is building CWI memory activation.", relevance: 0.82f, confidence: 0.82f, contentSha: "same-sha");
        var stronger = Memory("mem-stronger", "User is building CWI memory activation.", relevance: 0.96f, confidence: 0.91f, contentSha: "same-sha");

        var result = service.BuildActiveContext(context, new[] { weaker, stronger }, limit: 10);

        result.ActiveMemories.Should().ContainSingle(memory => memory.MemoryId == "mem-stronger");
        result.Decisions.Should().Contain(decision =>
            decision.MemoryId == "mem-weaker"
            && decision.Outcome == "excluded"
            && decision.Reason == "duplicate_suppressed");
    }

    [Fact]
    public void BuildActiveContext_excludes_deleted_retracted_expired_and_superseded_memory()
    {
        var service = new MemoryFrameworkService();
        var context = CreateContext("what projects am I working on?");
        var old = Memory("mem-old", "User is working on the old dashboard path.");
        var replacement = Memory("mem-new", "updates: mem-old User is working on the governed memory bridge.");
        var deleted = Memory("mem-deleted", "deleted: User no longer wants this preference used.");
        var retracted = Memory("mem-retracted", "retracted: This relationship note was wrong.");
        var expired = Memory("mem-expired", "expired: This temporary project status is no longer valid.");

        var result = service.BuildActiveContext(
            context,
            new[] { old, replacement, deleted, retracted, expired },
            limit: 10);

        result.ActiveMemories.Should().ContainSingle(memory => memory.MemoryId == "mem-new");
        result.Decisions.Should().Contain(decision => decision.MemoryId == "mem-old" && decision.Reason == "superseded_by_newer_memory");
        result.Decisions.Should().Contain(decision => decision.MemoryId == "mem-deleted" && decision.Reason == "temporal_state_deleted");
        result.Decisions.Should().Contain(decision => decision.MemoryId == "mem-retracted" && decision.Reason == "temporal_state_retracted");
        result.Decisions.Should().Contain(decision => decision.MemoryId == "mem-expired" && decision.Reason == "temporal_state_expired");
    }

    [Fact]
    public void BuildActiveContext_surfaces_unresolved_conflicts_without_treating_them_as_active_memory()
    {
        var service = new MemoryFrameworkService();
        var context = CreateContext("when does this warranty expire?");
        var conflict = Memory(
            "mem-conflict",
            "conflict: One source says the dryer warranty expires in May and another says it expires in June.",
            sourceType: "activated_user_memory conflict warranty");

        var result = service.BuildActiveContext(context, new[] { conflict }, limit: 10);

        result.ActiveMemories.Should().BeEmpty();
        result.Conflicts.Should().ContainSingle(conflictSummary =>
            conflictSummary.MemoryIds.Contains("mem-conflict")
            && conflictSummary.Summary.Contains("Unresolved memory conflict", StringComparison.OrdinalIgnoreCase));
        result.Decisions.Should().Contain(decision =>
            decision.MemoryId == "mem-conflict"
            && decision.Outcome == "excluded"
            && decision.Reason == "unresolved_conflict");
    }

    [Fact]
    public void BuildActiveContext_enforces_user_scope_while_allowing_explicit_tenant_memory()
    {
        var service = new MemoryFrameworkService();
        var context = CreateContext("what do you know about me?");
        var currentUser = Memory("mem-user", "User is working on CWI response frameworks.");
        var otherUser = Memory("mem-other", "Other user private memory.", userId: "other-user");
        var unscopedImport = Memory("mem-unscoped", "Unscoped imported memory should not be shared.", userId: null);
        var tenantPolicy = Memory(
            "mem-tenant",
            "Tenant memory says response handoffs include acceptance criteria.",
            userId: null,
            memoryScope: "tenant",
            sourceType: "tenant policy");

        var result = service.BuildActiveContext(
            context,
            new[] { currentUser, otherUser, unscopedImport, tenantPolicy },
            limit: 10);

        result.ActiveMemories.Should().Contain(memory => memory.MemoryId == "mem-user");
        result.ActiveMemories.Should().Contain(memory => memory.MemoryId == "mem-tenant");
        result.ActiveMemories.Should().NotContain(memory => memory.MemoryId == "mem-other");
        result.ActiveMemories.Should().NotContain(memory => memory.MemoryId == "mem-unscoped");
        result.Decisions.Should().Contain(decision => decision.MemoryId == "mem-other" && decision.Reason == "scope_not_permitted");
        result.Decisions.Should().Contain(decision => decision.MemoryId == "mem-unscoped" && decision.Reason == "scope_not_permitted");
    }

    [Fact]
    public void BuildActiveContext_filters_low_confidence_and_low_relevance_candidates()
    {
        var service = new MemoryFrameworkService();
        var context = CreateContext("what do you know about me?");
        var lowConfidence = Memory("mem-low-confidence", "User is working on CWI.", confidence: 0.60f);
        var lowRelevance = Memory("mem-low-relevance", "User is working on CWI.", relevance: 0.60f);
        var active = Memory("mem-active", "User is working on CWI memory framework.");

        var result = service.BuildActiveContext(context, new[] { lowConfidence, lowRelevance, active }, limit: 10);

        result.ActiveMemories.Should().ContainSingle(memory => memory.MemoryId == "mem-active");
        result.Decisions.Should().Contain(decision =>
            decision.MemoryId == "mem-low-confidence"
            && decision.Reason == "confidence_below_active_context_threshold");
        result.Decisions.Should().Contain(decision =>
            decision.MemoryId == "mem-low-relevance"
            && decision.Reason == "relevance_below_active_context_threshold");
    }

    private static AskWorkflowContext CreateContext(string question)
    {
        return new AskWorkflowContext("tenant-1", "user-1", question, "personal", null, "observe", 5)
        {
            QueryClassification = "memory_graph_request",
            IntentType = "memory_recall",
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "user_memory_graph",
            RetrievalDomainTerms = new[] { "user" },
            EvidenceRequired = true
        };
    }

    private static GovernedMemoryResult Memory(
        string memoryId,
        string text,
        string? userId = "user-1",
        string memoryScope = "activated_user_memory",
        string sourceType = "activated_user_memory work_career project_memory",
        float relevance = 0.92f,
        float confidence = 0.90f,
        string? contentSha = null)
    {
        return new GovernedMemoryResult
        {
            MemoryId = memoryId,
            MemoryScope = memoryScope,
            SourceType = sourceType,
            Text = text,
            TenantId = "tenant-1",
            UserId = userId,
            RelevanceScore = relevance,
            Confidence = confidence,
            ContentSha256 = contentSha ?? $"{memoryId}-sha",
            ProvenanceId = $"prov-{memoryId}"
        };
    }
}
#nullable disable
