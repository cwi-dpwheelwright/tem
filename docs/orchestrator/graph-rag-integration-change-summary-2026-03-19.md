# GraphRAG Integration Change Summary — 2026-03-19

Prepared for: CWI team handoff  
Prepared by: Codex  
Source handoff reviewed: `/Users/astrion9/Documents/CWI_Codex_Backlog_Handoff_2026-03-19.md`

## Summary

This change set implements the first GraphRAG integration slice described in the March 19 backlog handoff.

The main outcome is that `cwi-orchestrator` now has a graph-aware ask path that can:

- load graph configuration,
- plan graph-first queries for a small initial template set,
- query Apache AGE through Postgres,
- merge graph evidence into the ask workflow before vector retrieval,
- expose graph-aware trace fields and reasoning receipt metadata,
- surface graph/document evidence counts in the UI trace panel.

This work also tightened one major UI integration issue: the GPT UI ask route previously short-circuited to a direct local LLM path whenever `CWI_LLM_*` variables were present. That behavior is now explicit opt-in instead of automatic.

## Repo Review Findings Applied Before Changes

The backlog handoff was directionally useful, but several referenced paths did not match the current repo layout. The implementation used the repo as the source of truth.

Observed mismatches:

- `cwi-node-ui-web` in the handoff maps to `src/frontend/web-ui/cwi-gpt-web-ui/` in this repo.
- `tools/cwi-teacher-node/` in the handoff maps to `tools/teacher-node/`.
- `ops/cwi-ops/` in the handoff maps to `OPS/cwi-ops/`.

Additional repo findings that affected the implementation:

- The orchestrator already had adopted/legacy pack compatibility support from the March 12 work.
- Graph DB connectivity already existed in the API health path, but not in the ask workflow.
- The UI ask route had a local direct-to-LLM fallback that could bypass the orchestrator entirely.

## Completed Backend Changes

### 1. Added graph configuration support to the orchestrator

Graph-specific settings were added through API config, composition, and application options:

- `CWI_GRAPH_ENABLED`
- `CWI_GRAPH_DB_DSN`
- `CWI_GRAPH_NAME`
- `CWI_GRAPH_QUERY_TIMEOUT_SECONDS`
- `CWI_GRAPH_TOP_K`

Compatibility improvement:

- Graph name resolution now accepts `CWI_GRAPH_NAME` or `CWI_GRAPH_DB_NAME`, so the orchestrator aligns better with existing ETL and ops conventions already used elsewhere in the repo.

Files updated:

- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api/Configuration/OrchestratorConfig.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api/Program.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Composition/DependencyInjection.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/ApplicationOptions.cs`

### 2. Added initial graph evidence models and AGE adapter

New graph-layer files were added under the orchestrator application:

- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/GraphEvidence.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/GraphQueryPlanner.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/AgeGraphEvidenceProvider.cs`

What these do:

- define graph evidence and reasoning receipt models,
- detect graph-worthy questions using initial keyword/template rules,
- generate Cypher for:
  - equipment/metrics,
  - maintenance attention,
  - procedure-family relationships,
- execute AGE queries through `Npgsql`,
- normalize returned values for use in the orchestrator ask flow.

Implementation notes:

- `Npgsql` was added to the orchestrator application project.
- The AGE adapter was hardened to cast returned `agtype` columns to text before reading them.
- Returned AGE values are normalized before being mapped into `GraphEvidence`.

### 3. Integrated graph retrieval into the ask workflow

The ask workflow now supports graph evidence before vector retrieval.

Changes completed:

- registered `GraphQueryPlanner`,
- registered `IGraphEvidenceProvider`,
- inserted `AskGraphRetrievalStep` into the ask workflow before `AskRetrievalStep`,
- extended `AskWorkflowContext` with graph evidence, graph query metadata, and fallback state.

Files updated:

- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/DependencyInjection.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Workflows/AskWorkflow.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Workflows/Steps/AskWorkflowSteps.cs`

### 4. Updated prompt construction for graph + document evidence

The ask prompt builder now includes separate graph and document sections instead of treating all evidence as a single flat list.

Prompt behavior added:

- emits a `Graph evidence:` section when graph hits exist,
- emits a `Document evidence:` section for vector retrieval results,
- tells the model to distinguish direct graph facts from inferred relationships,
- preserves document citation guidance.

File updated:

- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Ask/PromptBuilder.cs`

### 5. Expanded trace payload and response shaping

The ask response trace now includes graph-aware metadata:

- `graph_hits`
- `document_hits`
- `reasoning_receipt`

The response step now also:

- merges graph and document source identifiers,
- computes a simple heuristic confidence score,
- records whether graph fallback behavior was used,
- includes the selected graph query template and extracted graph terms in the reasoning receipt.

Files updated:

- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Models/TracePayload.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Workflows/Steps/AskWorkflowSteps.cs`

## Completed Frontend Changes

### 6. Extended the GPT UI trace contract

The UI trace type now understands:

- `graph_hits`
- `document_hits`
- `reasoning_receipt`

File updated:

- `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/api/types.ts`

### 7. Updated the trace panel to display graph-aware metadata

The GPT UI trace panel now renders:

- graph hit count,
- document hit count,
- graph template,
- graph fallback/failure reason when fallback occurred.

File updated:

- `src/frontend/web-ui/cwi-gpt-web-ui/src/components/trace-panel.tsx`

### 8. Made direct UI-to-LLM ask behavior explicit opt-in

Previously, the GPT UI ask route would call the local LLM path whenever `CWI_LLM_BASE_URL` and `CWI_LLM_MODEL` were set. That could bypass the orchestrator and prevent graph evidence from ever being used.

Completed change:

- the direct LLM path now requires `CWI_UI_DIRECT_LLM_ENABLED=true`,
- otherwise the default ask route proxies through the gateway/orchestrator.

Files updated:

- `src/frontend/web-ui/cwi-gpt-web-ui/src/app/api/_shared/llm.ts`
- `src/frontend/web-ui/cwi-gpt-web-ui/src/app/api/ask/route.ts`

## Completed Test Changes

New or updated orchestrator unit tests were added for the new graph-aware path.

Files added or updated:

- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Ask/PromptBuilderTests.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Workflows/AskGraphRetrievalStepTests.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Workflows/AskResponseStepTests.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Action/ActionServiceTests.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Packs/PackResolverTests.cs`

What these tests cover:

- prompt includes graph and document evidence sections,
- graph retrieval step records evidence when the provider succeeds,
- graph retrieval step degrades cleanly when the provider fails,
- response step emits graph/document counts and reasoning receipt metadata,
- existing `ApplicationOptions` callers still compile with the new graph config constructor shape.

## Files Touched

### Backend

- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api/Configuration/OrchestratorConfig.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api/Program.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/ApplicationOptions.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Ask/PromptBuilder.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Cwi.Orchestrator.Application.csproj`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/DependencyInjection.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Models/TracePayload.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Workflows/AskWorkflow.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Workflows/Steps/AskWorkflowSteps.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Composition/DependencyInjection.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/AgeGraphEvidenceProvider.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/GraphEvidence.cs`
- `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/GraphQueryPlanner.cs`

### Frontend

- `src/frontend/web-ui/cwi-gpt-web-ui/src/app/api/_shared/llm.ts`
- `src/frontend/web-ui/cwi-gpt-web-ui/src/app/api/ask/route.ts`
- `src/frontend/web-ui/cwi-gpt-web-ui/src/components/trace-panel.tsx`
- `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/api/types.ts`

### Tests

- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Action/ActionServiceTests.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Ask/PromptBuilderTests.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Packs/PackResolverTests.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Workflows/AskGraphRetrievalStepTests.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Workflows/AskResponseStepTests.cs`

## Validation Status

Implementation status:

- Backend GraphRAG integration slice: completed
- UI trace contract updates: completed
- UI direct LLM bypass hardening: completed
- Unit test additions/updates: completed

Execution status:

- `dotnet build`: not run successfully on this machine
- `dotnet test`: not run successfully on this machine
- frontend typecheck/tests: not run successfully on this machine

Reason:

- the repo pins `.NET SDK 8.0.100` in `global.json`,
- the current machine only has `.NET SDK 10.0.201`,
- `node` and `npm` are not installed on this machine.

## Known Follow-Up Work

The following items remain after this completed slice:

1. Run backend build and unit tests in an environment with .NET 8 available.
2. Run frontend typecheck/tests in an environment with Node installed.
3. Validate the AGE adapter against a live graph with real `agtype` responses.
4. Verify whether `CWI_GRAPH_DB_DSN` should remain a startup-required env var even when `CWI_GRAPH_ENABLED=false`.
5. Decide whether to add deeper graph query templates beyond the initial equipment/metrics, maintenance, and procedure-family set.
6. Decide whether to surface richer graph provenance details in the UI beyond the current trace panel additions.

## Recommended Next Step

Provision a validation environment with:

- `.NET SDK 8.x`,
- Node/npm,
- access to a live or test Apache AGE instance,

then run the orchestrator and GPT UI test/build loops before taking the next backlog item.
