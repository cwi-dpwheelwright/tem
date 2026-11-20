using System;
using System.Threading;
using System.Threading.Tasks;
using Cwi.Common;
using Cwi.Orchestrator.Application.Graph;
using Cwi.Orchestrator.Application.Journey;
using Cwi.Orchestrator.Application.Memory;
using Cwi.Orchestrator.Application.Models;
using Cwi.Orchestrator.Application.Packs;
using Cwi.Orchestrator.Application.Retrieval;
using Cwi.Orchestrator.Application.Trace;
using Cwi.Orchestrator.Application.TrustedWeb;
using Cwi.Orchestrator.Application.Workflows;
using Cwi.Orchestrator.Application.Workflows.Steps;
using FluentAssertions;
using Xunit;

namespace Cwi.Orchestrator.UnitTests.Workflows;

public sealed class AskResponseStepTests
{
    [Fact]
    public async Task ExecuteAsync_populates_pack_hash_and_version()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "question", "pack", null, "observe", 3)
        {
            ModelName = "model-1",
            PackHash = "pack-hash-1",
            PackVersion = "1.2.3"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Trace.RequestId.Should().Be(journeyContext.RequestId);
        response.Data!.Trace.PackHash.Should().Be("pack-hash-1");
        response.Data.Trace.PackVersion.Should().Be("1.2.3");
        response.Data.Trace.PackHashes.Should().ContainKey("pack");
        response.Data.Trace.PackHashes["pack"].Should().Be("pack-hash-1");
    }

    [Fact]
    public async Task ExecuteAsync_populates_graph_counts_and_reasoning_receipt()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "question", "pack", null, "observe", 3)
        {
            ModelName = "model-1",
            GraphQueryTemplate = "equipment_metrics",
            GraphQueryTerms = new[] { "micromat", "amperage" }
        };
        context.GraphEvidence.Add(new GraphEvidence
        {
            Subject = "Micromat",
            Relation = "MEASURED_BY",
            Object = "amperage",
            Template = "equipment_metrics",
            SourceDocument = "HDPE Material Test Report 073125V2.docx"
        });
        context.Retrieved.Add(new RetrievalResult("doc-1", "chunk-1", "evidence", 0.88f));

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.Trace.RequestId.Should().Be(journeyContext.RequestId);
        response.Data!.Trace.GraphHits.Should().Be(1);
        response.Data.Trace.DocumentHits.Should().Be(1);
        response.Data.Trace.Confidence.Should().Be(0.85f);
        response.Data.Trace.Grounded.Should().BeTrue();
        response.Data.Trace.ReasoningBased.Should().BeFalse();
        response.Data.Trace.ReasoningReceipt.Should().NotBeNull();
        response.Data.Trace.ReasoningReceipt!.GraphUsed.Should().BeTrue();
        response.Data.Trace.ReasoningReceipt.GraphQueryTemplate.Should().Be("equipment_metrics");
        response.Data.Trace.Sources.Should().Contain(source => source.StartsWith("graph:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_includes_home_node_execution_metadata()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "question", "pack", null, "observe", 3)
        {
            Answer = "queued",
            ExecutionState = "pending",
            ExecutionTarget = "home_node",
            DeferredWorkId = "work-123",
            HomeNodeId = "home-node-01",
            HomeNodeOwnerUserId = "user",
            HomeNodeTailscaleIp = "100.122.244.48",
            HomeNodeMembershipStatus = "active",
            HomeNodeMembershipLastSeenUtc = DateTimeOffset.Parse("2026-04-22T20:00:00Z"),
            HomeNodeStatus = "active",
            ExecutionMessage = "Queued for local execution.",
            HomeNodeSources = new[]
            {
                new HomeNodeSourceDto
                {
                    SourceKind = "bundle",
                    Title = "notes/cooling.md",
                    Path = "/var/cwi/bundles/notes/cooling.md",
                    Score = 9,
                    Excerpt = "Ceramic cooling protects the Atlas engine."
                }
            },
            HomeNodeExecutionMode = "local_artifact_search",
            HomeNodeLocalPolicyOutcome = "ALLOW_LOCAL_ONLY_NO_EGRESS",
            HomeNodeBundleId = "bundle-thermal",
            HomeNodePackId = "thermal-pack",
            HomeNodePackName = "Thermal Operations",
            HomeNodePackAnchor = "anchor-123",
            HomeNodeSearchTerms = new[] { "atlas", "cooling" }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data.Should().NotBeNull();
        response.Data!.ExecutionState.Should().Be("pending");
        response.Data.ExecutionTarget.Should().Be("home_node");
        response.Data.DeferredWorkId.Should().Be("work-123");
        response.Data.HomeNodeStatus.Should().Be("active");
        response.Data.ExecutionMessage.Should().Be("Queued for local execution.");
        response.Data.HomeNodeSources.Should().NotBeNull();
        response.Data.HomeNodeSources![0].Title.Should().Be("notes/cooling.md");
        response.Data.Trace.ExecutionState.Should().Be("pending");
        response.Data.Trace.ExecutionTarget.Should().Be("home_node");
        response.Data.Trace.DeferredWorkId.Should().Be("work-123");
        response.Data.Trace.HomeNodeId.Should().Be("home-node-01");
        response.Data.Trace.HomeNodeOwnerUserId.Should().Be("user");
        response.Data.Trace.HomeNodeTailscaleIp.Should().Be("100.122.244.48");
        response.Data.Trace.HomeNodeMembershipStatus.Should().Be("active");
        response.Data.Trace.HomeNodeMembershipLastSeenUtc.Should().Be(DateTimeOffset.Parse("2026-04-22T20:00:00Z"));
        response.Data.Trace.HomeNodeStatus.Should().Be("active");
        response.Data.Trace.HomeNodeSourceCount.Should().Be(1);
        response.Data.Trace.HomeNodeExecutionMode.Should().Be("local_artifact_search");
        response.Data.Trace.HomeNodeLocalPolicyOutcome.Should().Be("ALLOW_LOCAL_ONLY_NO_EGRESS");
        response.Data.Trace.HomeNodeBundleId.Should().Be("bundle-thermal");
        response.Data.Trace.HomeNodePackId.Should().Be("thermal-pack");
        response.Data.Trace.HomeNodePackName.Should().Be("Thermal Operations");
        response.Data.Trace.HomeNodePackAnchor.Should().Be("anchor-123");
        response.Data.Trace.HomeNodeSearchTerms.Should().BeEquivalentTo(new[] { "atlas", "cooling" });
        response.Data.Trace.Sources.Should().Contain("home_node:notes/cooling.md");
        response.Data.Trace.Grounded.Should().BeTrue();
        response.Data.Trace.ReasoningBased.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_applies_governed_confidence_floor_for_identity_queries()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "What is CWI?", "pack", null, "observe", 3)
        {
            Answer = "Christian Wheelwright Intelligence.",
            QueryClassification = "identity_definition",
            QueryClassificationReason = "Matched governed force-query pattern.",
            RetrievalAllowed = false,
            GroundingMode = "system_truth",
            RetrievalSkippedReason = "system_truth_uses_governed_identity_pack",
            AudienceMode = "home_user",
            VerbosityMode = "detailed",
            CommunicationCoreApplied = true,
            ModelName = "model-1",
            CorePackExecutionOrder = CorePackOrdering.CorePackOrder,
            FinalLayerExecutionOrder = CorePackOrdering.FinalLayerOrder,
            BaselinePackGuidance = new[]
            {
                new BaselinePackGuidance(
                    "cwi-identity",
                    "1.0.0",
                    "hash-identity",
                    "CWI stands for Christian Wheelwright Intelligence.",
                    moduleRole: "system_truth",
                    confidenceFloor: 0.95f),
                new BaselinePackGuidance(
                    "cwi-communication-core",
                    "1.0.0",
                    "hash-communication",
                    "Be direct.",
                    moduleRole: "global_communication_layer",
                    applyStage: "final_response_composition")
            }
        };
        context.Retrieved.Add(new RetrievalResult("repo-css-doc", "chunk-1", "CSS build context", 0.99f));

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Trace.Confidence.Should().Be(0.95f);
        response.Data.Trace.Grounded.Should().BeTrue();
        response.Data.Trace.ReasoningBased.Should().BeFalse();
        response.Data.Trace.QueryClassification.Should().Be("identity_definition");
        response.Data.Trace.CommunicationCoreApplied.Should().BeTrue();
        response.Data.Trace.CommunicationCoreVersion.Should().Be("1.0.0");
        response.Data.Trace.GovernedSourceCount.Should().Be(2);
        response.Data.Trace.CorePackOrder.Should().Equal(CorePackOrdering.CorePackOrder);
        response.Data.Trace.DomainPacks.Should().Equal("pack");
        response.Data.Trace.FinalLayers.Should().Equal(CorePackOrdering.FinalLayerOrder);
        response.Data.Trace.FinalAnswerHash.Should().NotBeNullOrWhiteSpace();
        response.Data.Trace.DocumentHits.Should().Be(0);
        response.Data.Trace.GroundingMode.Should().Be("system_truth");
        response.Data.Trace.Grounded.Should().BeTrue();
        response.Data.Citations.Should().Contain(citation => citation.DocId == "cwi-identity");
        response.Data.Citations.Should().NotContain(citation => citation.DocId == "repo-css-doc");
        response.Data.Trace.Sources.Should().Contain("pack:cwi-identity@1.0.0");
        response.Data.Trace.Sources.Should().NotContain(source => source.Contains("repo-css-doc", StringComparison.OrdinalIgnoreCase));
        response.Data.Trace.PackHashes.Should().ContainKey("cwi-identity");
        response.Data.Trace.PackHashes["cwi-identity"].Should().Be("hash-identity");
    }

    [Fact]
    public async Task ExecuteAsync_marks_empty_evidence_answers_as_reasoning_based()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "question", "pack", null, "observe", 3)
        {
            Answer = "A model-reasoned answer.",
            ModelName = "model-1"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Trace.Confidence.Should().Be(0.8f);
        response.Data.Trace.ConfidenceBasis.Should().Be("ungrounded_model_reasoning");
        response.Data.Trace.Grounded.Should().BeFalse();
        response.Data.Trace.ReasoningBased.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_includes_presentation_framework_trace_metadata()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "what is at risk right now", "pack", null, "observe", 3)
        {
            Answer = "Risks:\n1. Delivery risk.\n\nImpact:\nLaunch may move.\n\nNext action:\nConfirm owner.",
            ModelName = "model-1",
            TemplateUsed = "risk_summary",
            PresentationFrameworkVersion = PresentationFrameworkService.Version,
            PresentationTemplateFamily = "risk",
            PresentationTemplateReason = "template_risk",
            PresentationRhythmMode = "rhythm_structured",
            PresentationRhythmReason = "rhythm_response_mode_forced",
            PresentationFormattingMode = "format_structured_sections",
            PresentationFormattingReason = "format_response_mode_contract",
            PresentationSourceDisplayMode = "source_display_none",
            PresentationSourceDisplayReason = "source_display_no_sources_used"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Trace.PresentationFrameworkVersion.Should().Be(PresentationFrameworkService.Version);
        response.Data.Trace.PresentationTemplateFamily.Should().Be("risk");
        response.Data.Trace.PresentationTemplateReason.Should().Be("template_risk");
        response.Data.Trace.PresentationRhythmMode.Should().Be("rhythm_structured");
        response.Data.Trace.PresentationRhythmReason.Should().Be("rhythm_response_mode_forced");
        response.Data.Trace.PresentationFormattingMode.Should().Be("format_structured_sections");
        response.Data.Trace.PresentationFormattingReason.Should().Be("format_response_mode_contract");
        response.Data.Trace.PresentationSourceDisplayMode.Should().Be("source_display_none");
        response.Data.Trace.PresentationSourceDisplayReason.Should().Be("source_display_no_sources_used");
    }

    [Fact]
    public async Task ExecuteAsync_reports_general_model_knowledge_route_without_pack_usage()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "what is oceaneering", "enterprise", null, "observe", 3)
        {
            Answer = "Oceaneering is an engineering and applied technology company.",
            QueryClassification = "general_model_knowledge",
            QueryClassificationReason = "General knowledge question does not require enterprise source retrieval.",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            RetrievalSkippedReason = "general_knowledge_does_not_require_enterprise_retrieval",
            ModelName = "model-1",
            BaselinePackGuidance = new[]
            {
                new BaselinePackGuidance(
                    "cwi-response-orchestrator",
                    "1.1.0",
                    "hash-1",
                    "Enforce concise response behavior.")
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Trace.Route.Should().Be("general_model_knowledge");
        response.Data.Trace.PackUsed.Should().BeFalse();
        response.Data.PacksUsed.Should().BeNull();
        response.Data.Trace.Sources.Should().BeEmpty();
        response.Data.Trace.GroundingMode.Should().Be("non_grounded");
        response.Data.Trace.ReasoningBased.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_gives_simple_arithmetic_high_confidence_without_grounding()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "What is 2 + 2?", "pack", null, "observe", 3)
        {
            Answer = "2 + 2 = 4.",
            QueryClassification = "deterministic_reasoning",
            QueryClassificationReason = "Detected simple deterministic arithmetic.",
            RetrievalAllowed = false,
            GroundingMode = "non_grounded",
            RetrievalSkippedReason = "deterministic_reasoning_does_not_require_sources",
            ModelName = "model-1"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Trace.Confidence.Should().Be(0.95f);
        response.Data.Trace.ConfidenceBasis.Should().Be("deterministic_reasoning_non_grounded");
        response.Data.Trace.GroundingMode.Should().Be("non_grounded");
        response.Data.Trace.Grounded.Should().BeFalse();
        response.Data.Trace.Sources.Should().BeEmpty();
        response.Data.Trace.ReasoningReceipt!.Grounded.Should().BeFalse();
        response.Data.Trace.ReasoningReceipt.GroundingMode.Should().Be("non_grounded");
    }

    [Fact]
    public async Task ExecuteAsync_marks_evidence_required_without_grounding_as_blocked()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "According to our docs, what does the unknown subsystem do?", "pack", null, "observe", 3)
        {
            Answer = "I do not have enough governed evidence to answer that safely.",
            QueryClassification = "general_reasoning",
            EvidenceRequired = true,
            RetrievalAllowed = false,
            GroundingMode = "source_grounded",
            RetrievalSkippedReason = "evidence_required_but_no_governed_evidence",
            TemplateUsed = "low_confidence_escalation",
            ToneMode = "careful_escalation",
            FallbackApplied = true,
            ModelName = "governed-evidence-gate"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Confidence.Should().Be(0.2f);
        response.Data.ConfidenceLevel.Should().Be("blocked");
        response.Data.Trace.ConfidenceBasis.Should().Be("evidence_required_without_grounded_evidence");
        response.Data.Trace.Grounded.Should().BeFalse();
        response.Data.Trace.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_preserves_risk_response_mode_when_final_evidence_gate_runs()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "what's at risk right now", "enterprise", null, "observe", 3)
        {
            Answer = "The enterprise policy framework says to evaluate risk posture before proceeding.",
            QueryClassification = "memory_graph_request",
            IntentType = "risk_review",
            EvidenceRequired = true,
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            TemplateUsed = "risk_summary",
            ToneMode = "risk_concise",
            ModelName = "model-1"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().Contain("Risks:");
        response.Data.Answer.Should().Contain("Impact:");
        response.Data.Answer.Should().Contain("Next action:");
        response.Data.Answer.Should().NotContain("policy framework");
        response.Data.TemplateUsed.Should().Be("risk_summary");
        response.Data.Trace.TemplateUsed.Should().Be("risk_summary");
        response.Data.Trace.ToneMode.Should().Be("risk_concise");
        response.Data.ConfidenceLevel.Should().Be("blocked");
        response.Data.Trace.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_preserves_daily_briefing_response_mode_when_final_evidence_gate_runs()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "daily briefing", "enterprise", null, "observe", 3)
        {
            Answer = "The enterprise policy framework says to review priorities under the governance process.",
            QueryClassification = "memory_graph_request",
            IntentType = "daily_briefing",
            EvidenceRequired = true,
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            TemplateUsed = "daily_briefing",
            ToneMode = "briefing_concise",
            ModelName = "model-1"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().Contain("Priorities:");
        response.Data.Answer.Should().Contain("Risk:");
        response.Data.Answer.Should().Contain("Recommendation:");
        response.Data.Answer.Should().NotContain("policy framework");
        response.Data.TemplateUsed.Should().Be("daily_briefing");
        response.Data.Trace.TemplateUsed.Should().Be("daily_briefing");
        response.Data.Trace.ToneMode.Should().Be("briefing_concise");
        response.Data.ConfidenceLevel.Should().Be("blocked");
        response.Data.Trace.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_keeps_evidence_required_memory_only_response_blocked()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "According to our docs, what does the unknown subsystem do?", "pack", null, "observe", 3)
        {
            Answer = "I do not have enough governed evidence to answer that safely.",
            QueryClassification = "general_reasoning",
            EvidenceRequired = true,
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalSkippedReason = "evidence_required_but_no_governed_evidence",
            TemplateUsed = "low_confidence_escalation",
            ToneMode = "careful_escalation",
            FallbackApplied = true,
            ModelName = "governed-evidence-gate",
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "hist-prior-failure",
                    MemoryScope = "conversation",
                    SourceType = "conversation_turn",
                    Text = "Prior turn: CWI answered that there was not enough governed evidence.",
                    TenantId = "tenant",
                    UserId = "user",
                    RelevanceScore = 0.99f,
                    Confidence = 0.90f
                }
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Confidence.Should().Be(0.2f);
        response.Data.ConfidenceLevel.Should().Be("blocked");
        response.Data.Trace.ConfidenceBasis.Should().Be("evidence_required_without_grounded_evidence");
        response.Data.Trace.Grounded.Should().BeFalse();
        response.Data.Trace.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_overrides_stale_grounded_flag_for_evidence_required_memory_only_response()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "According to our docs, what does the unknown subsystem do?", "pack", null, "observe", 3)
        {
            Answer = """
                Intent identification: source-grounded answer.
                Response template: detailed answer.
                The prior response said this subsystem failed closed.
                """,
            QueryClassification = "general_reasoning",
            EvidenceRequired = true,
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            Grounded = true,
            TemplateUsed = "general_answer",
            ModelName = "model-1",
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "hist-prior-failure",
                    MemoryScope = "conversation",
                    SourceType = "conversation_turn",
                    Text = "Prior turn: CWI answered that there was not enough governed evidence.",
                    TenantId = "tenant",
                    UserId = "user",
                    RelevanceScore = 0.99f,
                    Confidence = 0.90f
                }
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().Be("I do not have enough governed evidence to answer that safely. Please name the source domain, runbook, repo area, log, or CWI component you want me to use, and I can retry with governed retrieval.");
        response.Data.Answer.Should().NotContain("Intent identification");
        response.Data.Confidence.Should().Be(0.2f);
        response.Data.ConfidenceLevel.Should().Be("blocked");
        response.Data.Trace.Grounded.Should().BeFalse();
        response.Data.Trace.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_marks_local_model_without_sources_as_non_grounded_reasoning()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "What is 2 + 2?", "pack", null, "observe", 3)
        {
            Answer = "4",
            ExecutionTarget = "home_node",
            HomeNodeStatus = "completed",
            HomeNodeExecutionMode = "local_model_ollama",
            ModelName = "home-node"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Trace.Confidence.Should().Be(0.8f);
        response.Data.Trace.Grounded.Should().BeFalse();
        response.Data.Trace.ReasoningBased.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_marks_preference_updates_as_memory_grounded_without_sources()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "My preferred output is step-by-step instructions.", "pack", null, "observe", 3)
        {
            Answer = "Preference saved.",
            QueryClassification = "preference_update",
            QueryClassificationReason = "Detected an operator preference statement.",
            RetrievalAllowed = false,
            GroundingMode = "memory_update",
            RetrievalSkippedReason = "preference_update_routes_to_memory",
            MemoryUpdated = true,
            ModelName = "model-1"
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Trace.Confidence.Should().Be(0.9f);
        response.Data.Trace.ConfidenceBasis.Should().Be("memory_update_confirmed");
        response.Data.Trace.MemoryUpdated.Should().BeTrue();
        response.Data.Trace.GroundingMode.Should().Be("memory_update");
        response.Data.Trace.Sources.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_includes_governed_memory_source_trace_and_update()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "Create a handoff.", "pack", null, "observe", 3)
        {
            ModelName = "model-1",
            MemoryResults = new[]
            {
                new GovernedMemoryResult
                {
                    MemoryId = "mem-123",
                    MemoryScope = "user",
                    SourceType = "conversation_summary",
                    TenantId = "tenant",
                    UserId = "user",
                    NodeId = "home-node-1",
                    RelevanceScore = 0.91f,
                    Confidence = 0.88f,
                    ContentSha256 = "hash-memory",
                    ProvenanceId = "prov-memory"
                }
            },
            SourceResults = new[]
            {
                new GovernedSourceResult
                {
                    SourceId = "src-123",
                    ChunkId = "chunk-004",
                    SourceType = "uploaded_source",
                    TenantId = "tenant",
                    UserId = "user",
                    NodeId = "home-node-1",
                    RelevanceScore = 0.87f,
                    ContentSha256 = "hash-source",
                    ExtractionMethod = "server_side_text_index"
                }
            },
            MemoryUpdate = new MemoryUpdateResult
            {
                Ignored = new[]
                {
                    new MemoryUpdateDecision
                    {
                        Reason = "temporary_or_not_durable"
                    }
                },
                ProvenanceId = "prov-answer"
            },
            RetrievalAllowed = true,
            GroundingMode = "source_grounded",
            RetrievalDomain = "handoff",
            RetrievalDomainTerms = new[] { "handoff" }
        };
        context.Retrieved.Add(new RetrievalResult("src-123", "chunk-004", "Relevant extracted source chunk about handoff style.", 0.87f));

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Trace.Confidence.Should().Be(0.87f);
        response.Data.Trace.MemoryResults.Should().ContainSingle(memory => memory.MemoryId == "mem-123");
        response.Data.Trace.SourceResults.Should().ContainSingle(source => source.SourceId == "src-123" && source.ChunkId == "chunk-004");
        response.Data.Trace.MemoryUpdate.Should().NotBeNull();
        response.Data.Trace.MemoryUpdate!.Ignored.Should().ContainSingle(decision => decision.Reason == "temporary_or_not_durable");
        response.Data.Trace.Sources.Should().Contain("memory:mem-123");
        response.Data.Trace.Sources.Should().Contain("source:src-123:chunk-004");
    }

    [Fact]
    public async Task ExecuteAsync_reports_trusted_web_trace_sources_and_citations_without_grounding()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "what is a public company", "pack", null, "observe", 3)
        {
            Answer = "A public company sells shares on public markets.",
            ModelName = "model-1",
            QueryClassification = "general_model_knowledge",
            GroundingMode = "non_grounded",
            RetrievalAllowed = false,
            TrustedWebContextRequested = true,
            TrustedWebContextEnabled = true,
            TrustedWebContextProfile = "general",
            TrustedWebContextRetrievalDurationMs = 42,
            TrustedWebResults = new[]
            {
                new TrustedWebResult
                {
                    SourceId = "trusted-site",
                    SourceName = "Trusted Site",
                    Url = "https://example.com/company",
                    Title = "Public company",
                    Snippet = "Public company context.",
                    ChunkId = "company",
                    Score = 0.82f,
                    RetrievedUtc = DateTimeOffset.Parse("2026-04-30T10:00:00Z")
                }
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().Contain("Trusted web sources consulted: [1] Public company.");
        response.Data.Answer.Should().NotContain("Web sources:");
        response.Data.Trace.TrustedWebRequested.Should().BeTrue();
        response.Data.Trace.TrustedWebEnabled.Should().BeTrue();
        response.Data.Trace.TrustedWebProfile.Should().Be("general");
        response.Data.Trace.TrustedWebHits.Should().Be(1);
        response.Data.Trace.RetrievalStatus.Should().Be("success");
        response.Data.Trace.TrustedWebRetrievalDurationMs.Should().Be(42);
        response.Data.Trace.TrustedWebSources.Should().ContainSingle(source =>
            source.SourceId == "trusted-site"
            && source.EvidenceSourceId == "web:trusted-site:company"
            && source.Marker == 1
            && source.Url == "https://example.com/company");
        response.Data.Trace.Sources.Should().Contain("web:trusted-site:company");
        response.Data.Citations.Should().Contain(citation =>
            citation.DocId == "web:trusted-site"
            && citation.ChunkId == "company"
            && citation.Marker == 1
            && citation.SourceId == "web:trusted-site:company"
            && citation.Score == 0.82f);
        response.Data.Trace.Grounded.Should().BeFalse();
        response.Data.Trace.ConfidenceBasis.Should().Be("non_grounded_general_reasoning");
    }

    [Fact]
    public async Task ExecuteAsync_appends_all_consulted_trusted_web_sources_when_answer_has_no_markers()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "what is the capital of texas", "pack", null, "observe", 3)
        {
            Answer = "The capital of Texas is Austin.",
            ModelName = "model-1",
            QueryClassification = "general_model_knowledge",
            GroundingMode = "non_grounded",
            TrustedWebContextRequested = true,
            TrustedWebContextEnabled = true,
            TrustedWebResults = new[]
            {
                new TrustedWebResult
                {
                    SourceId = "spb",
                    Url = "https://www.tspb.texas.gov/prop/tc/tc-history/history/",
                    Title = "SPB - Capitol History",
                    ChunkId = "capitol-history",
                    Score = 0.9f
                },
                new TrustedWebResult
                {
                    SourceId = "wikipedia-reference",
                    Url = "https://en.wikipedia.org/wiki/Austin,_Texas",
                    Title = "Austin, Texas - Wikipedia",
                    ChunkId = "austin-texas",
                    Score = 0.8f
                }
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().Contain("Trusted web sources consulted: [1] SPB - Capitol History; [2] Austin, Texas - Wikipedia.");
        response.Data.Citations.Should().HaveCount(2);
        response.Data.Trace.TrustedWebHits.Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_keeps_url_answer_compact_and_adds_trusted_web_marker()
    {
        var traceStore = new TraceLookupStore();
        var journeyContext = JourneyTestHelpers.CreateContext();
        var step = new AskResponseStep(traceStore, journeyContext);

        var context = new AskWorkflowContext("tenant", "user", "what changed", "pack", null, "observe", 3)
        {
            Answer = "The update is already cited from https://example.com/company.",
            ModelName = "model-1",
            GroundingMode = "non_grounded",
            TrustedWebContextRequested = true,
            TrustedWebContextEnabled = true,
            TrustedWebContextProfile = "general",
            TrustedWebResults = new[]
            {
                new TrustedWebResult
                {
                    SourceId = "trusted-site",
                    SourceName = "Trusted Site",
                    Url = "https://example.com/company",
                    Title = "Public company",
                    Snippet = "Public company context.",
                    ChunkId = "company",
                    Score = 0.82f
                }
            }
        };

        var response = await step.ExecuteAsync(
            context,
            () => Task.FromResult(ServiceResponse<AskResponseDto>.Success(new AskResponseDto())),
            CancellationToken.None);

        response.IsSuccess.Should().BeTrue();
        response.Data!.Answer.Should().NotContain("Web sources:");
        response.Data.Answer.Should().Contain("[1]");
        response.Data.Citations.Should().Contain(citation =>
            citation.DocId == "web:trusted-site"
            && citation.ChunkId == "company"
            && citation.Marker == 1);
    }
}
