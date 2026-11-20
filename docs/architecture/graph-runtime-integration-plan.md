# Graph Runtime Integration Plan

Date: 2026-03-20
Status: Repo-grounded implementation plan

## Objective

Complete and validate the existing graph-backed runtime path:

`cwi-gpt-web-ui -> cwi-gateway -> cwi-router -> cwi-orchestrator -> graphdb (+ vector store) -> model worker`

The plan assumes the graph already exists and is queryable. The work is to make the orchestrator and UI consume it correctly and verifiably.

## Current Repo State

The current worktree already includes the foundational patch set for graph-aware ask flow.

### Orchestrator changes already present in the worktree

- Graph config added to:
  - `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api/Configuration/OrchestratorConfig.cs`
  - `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Api/Program.cs`
  - `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Composition/DependencyInjection.cs`
  - `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/ApplicationOptions.cs`
- AGE/Postgres graph provider added under:
  - `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/AgeGraphEvidenceProvider.cs`
  - `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/GraphEvidence.cs`
  - `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Graph/GraphQueryPlanner.cs`
- Ask workflow graph retrieval and merge added under:
  - `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/DependencyInjection.cs`
  - `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Workflows/AskWorkflow.cs`
  - `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Workflows/Steps/AskWorkflowSteps.cs`
- Prompt composition updated in:
  - `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Ask/PromptBuilder.cs`
- Graph-aware trace fields added in:
  - `src/backend/services/cwi-orchestrator/src/Cwi.Orchestrator.Application/Models/TracePayload.cs`

### UI changes already present in the worktree

- Default ask path now prefers orchestrator and does not silently bypass to direct LLM unless explicitly enabled:
  - `src/frontend/web-ui/cwi-gpt-web-ui/src/app/api/_shared/llm.ts`
  - `src/frontend/web-ui/cwi-gpt-web-ui/src/app/api/ask/route.ts`
- Graph-aware trace fields are exposed in:
  - `src/frontend/web-ui/cwi-gpt-web-ui/src/lib/api/types.ts`
  - `src/frontend/web-ui/cwi-gpt-web-ui/src/components/trace-panel.tsx`

### Tests already added or updated in the worktree

- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Ask/PromptBuilderTests.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Workflows/AskGraphRetrievalStepTests.cs`
- `src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Workflows/AskResponseStepTests.cs`

## Remaining Work

## 1. Backend verification

Run orchestrator tests with the required SDK:

- Prerequisite: install .NET SDK `8.0.100` or another SDK accepted by `global.json`.
- Verify:
  - unit tests pass
  - graph retrieval step behaves correctly with and without graph availability
  - no fake citations are emitted during fallback

Recommended commands:

```bash
dotnet test src/backend/services/cwi-orchestrator/tests/Cwi.Orchestrator.UnitTests/Cwi.Orchestrator.UnitTests.csproj -c Release
```

## 2. Frontend verification

- Prerequisite: install Node and npm.
- Verify:
  - typecheck passes
  - unit tests pass
  - build passes
  - ask flow renders graph-aware trace fields

Recommended commands:

```bash
cd src/frontend/web-ui/cwi-gpt-web-ui
npm run typecheck
npm run test:unit
npm run build
```

## 3. Deployment wiring

Ensure the deployment environment includes the graph config the orchestrator now expects:

- `CWI_GRAPH_ENABLED=true`
- `CWI_GRAPH_DB_DSN=<postgres-age-dsn>`
- `CWI_GRAPH_NAME=<graph-name>`
- `CWI_GRAPH_QUERY_TIMEOUT_SECONDS=<seconds>`
- `CWI_GRAPH_TOP_K=<count>`

Relevant deployment files:

- `OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml`
- `OPS/cwi-ops/compose/full/docker-compose.full.yml`

## 4. End-to-end smoke path

Validate the full runtime path with a live environment:

1. Bring up the stack with graphdb, vector, router, orchestrator, and UI.
2. Ask a graph-shaped question through `cwi-gpt-web-ui`.
3. Confirm the response came through the orchestrator path, not direct LLM fallback.
4. Confirm `trace` includes:
   - `sources`
   - `confidence`
   - `graph_hits`
   - `document_hits`
   - `reasoning_receipt`
5. Confirm fallback behavior is explicit and does not fabricate graph evidence when graphdb is unavailable.

## 5. Broaden query-planning coverage

The current graph planner is intentionally narrow. Expand it based on observed query classes after the base path is verified.

Initial follow-up targets:

- additional equipment/metric patterns
- maintenance and troubleshooting patterns
- procedure/family and cross-pack bridge patterns
- evidence ranking when graph and vector results overlap

## 6. Gold-test coverage

Use existing `tools/cwi-gold-tests` rather than creating a parallel test harness.

Focus additions or activation on:

- graph-backed ask regression
- trace contract regression
- fallback-without-fabrication regression
- UI route contract regression for orchestrator-first ask behavior

## Acceptance Criteria

The graph runtime integration is complete when all are true:

1. `cwi-gpt-web-ui` reaches `cwi-orchestrator` by default for ask flow.
2. `cwi-orchestrator` can query graphdb directly when graph mode is enabled.
3. Ask responses include grounded trace data, not only freeform answer text.
4. UI surfaces show answer, sources, confidence, and graph-aware fields when available.
5. Graph fallback is logged and does not invent citations or graph receipts.
6. Unit and release-regression tests cover graph-enabled and graph-disabled behavior.

## Non-Goals

- Do not create a second orchestrator.
- Do not create a second user-facing Q&A UI.
- Do not create eight standalone graph microservices from stale handoff filenames.
- Do not pivot runtime graph logic to pack-manifest graph metadata unless that metadata is proven populated and authoritative.
