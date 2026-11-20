using System.Collections.Generic;

namespace Cwi.Orchestrator.Application.Runtime;

public sealed class RuntimeCapabilityStatusService
{
    public IReadOnlyList<RuntimeCapabilityStatusDto> GetMatrix()
    {
        return new[]
        {
            Capability(
                "teacher",
                RuntimeCapabilityStatus.Implemented,
                "Teacher tooling is present in the repository, but no live Teacher health evidence is evaluated here.",
                Evidence("repo", "tools/teacher-node/teacher_node.py"),
                Evidence("repo", "OPS/cwi-ops/DEPLOY/teacher_node/docker-compose.yml")),
            Capability(
                "critic",
                RuntimeCapabilityStatus.Implemented,
                "Critic-specific evaluation logic is externalized to the Critic node container. Orchestrator only sends observe-only snapshots to a configured Critic endpoint and records returned findings.",
                Evidence("repo", "tools/critic-node/critic_node.py"),
                Evidence("repo", "tools/critic-node/runtime_critic.py"),
                Evidence("repo", "OPS/cwi-ops/DEPLOY/critic_node/docker-compose.yml")),
            Capability(
                "process_miner",
                RuntimeCapabilityStatus.Implemented,
                "Local Process Miner observe-only runtime is implemented on the home node. It records sanitized observations locally and can create privacy-safe summaries without mutating memory or graph.",
                Evidence("repo", "src/node-agent-runtime/node_agent_runtime/process_miner.py")),
            Capability(
                "graph",
                RuntimeCapabilityStatus.Implemented,
                "Graph query and ingestion services are present, and graph evolution now creates observe-only update candidates before any graph mutation. Runtime database health is not evaluated by this matrix.",
                Evidence("repo", "src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/GraphQueryService.cs"),
                Evidence("repo", "src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/AgeImportedChatGraphIngestionService.cs"),
                Evidence("repo", "src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/GraphEvolutionService.cs")),
            Capability(
                "vector",
                RuntimeCapabilityStatus.Implemented,
                "Vector retrieval is implemented, and graph-to-vector refresh jobs are now tracked idempotently. Shard health must still come from the dependency health endpoint or metrics.",
                Evidence("repo", "src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Retrieval/VectorShardRetrievalProvider.cs"),
                Evidence("repo", "src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/GraphToVectorRefreshService.cs")),
            Capability(
                "truth_engine",
                RuntimeCapabilityStatus.Implemented,
                "Truth Engine arbitration is implemented in observe-only mode with trace and ledger metadata. Enforcement remains separately feature-flagged.",
                Evidence("repo", "src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Runtime/TruthEngineService.cs"),
                Evidence("repo", "src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Runtime/TruthEngineLedgerRecorder.cs")),
            Capability(
                "cache",
                RuntimeCapabilityStatus.Implemented,
                "Validated cache metadata and observe-only eligibility rules are implemented. Cache serving remains disabled unless enabled separately through selective enforcement.",
                Evidence("repo", "src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Runtime/ValidatedCacheService.cs"),
                Evidence("repo", "src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.DataAccess/Migrations/0013_validated_cache_records.sql")),
            Capability(
                "federated_learning",
                RuntimeCapabilityStatus.Implemented,
                "Privacy-safe federated learning summaries are implemented in policy-gated observe mode. Teacher submission requires explicit configuration and raw observations are not exported.",
                Evidence("repo", "src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Federated/FederatedLearningSummaryService.cs"),
                Evidence("repo", "tools/teacher-node/teacher_node.py")),
            Capability(
                "external_opinion",
                RuntimeCapabilityStatus.Implemented,
                "External Opinion advisory evaluation is implemented as a disabled-by-default, policy-gated QA lane. Results are ledgered as advisory evidence and never applied to user-facing answers.",
                Evidence("repo", "src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Runtime/ExternalOpinionService.cs"))
        };
    }

    private static RuntimeCapabilityStatusDto Capability(
        string id,
        RuntimeCapabilityStatus status,
        string detail,
        params RuntimeCapabilityEvidenceDto[] evidence)
    {
        return new RuntimeCapabilityStatusDto(
            Id: id,
            Status: status.Value,
            Detail: detail,
            Evidence: evidence);
    }

    private static RuntimeCapabilityEvidenceDto Evidence(string kind, string value)
    {
        return new RuntimeCapabilityEvidenceDto(kind, value);
    }
}

public sealed record RuntimeCapabilityStatusDto(
    string Id,
    string Status,
    string Detail,
    IReadOnlyList<RuntimeCapabilityEvidenceDto> Evidence);

public sealed record RuntimeCapabilityEvidenceDto(string Kind, string Value);

public sealed record RuntimeCapabilityStatus(string Value)
{
    public static readonly RuntimeCapabilityStatus NotImplemented = new("not_implemented");
    public static readonly RuntimeCapabilityStatus Implemented = new("implemented");
    public static readonly RuntimeCapabilityStatus Deployed = new("deployed");
    public static readonly RuntimeCapabilityStatus Healthy = new("healthy");
    public static readonly RuntimeCapabilityStatus Degraded = new("degraded");
    public static readonly RuntimeCapabilityStatus Unknown = new("unknown");
}
