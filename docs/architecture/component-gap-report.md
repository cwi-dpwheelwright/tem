# Component Gap Report

Date: 2026-03-20
Status: Repo-grounded gap assessment

## Executive Summary

There is no evidence that CWI needs a new router, orchestrator, pack service, gateway, or additional standalone graph apps. The current repo already covers the required current graph-runtime roles with existing components. The main remaining gaps are about runtime validation and a few phase-gated adoption decisions, not about missing core applications.

## Required-Now Roles

### Satisfied or composite-satisfied now

- `R2` Admin / glassbox / operator UI is already covered by a composite of `cwi-glassbox-web-ui` and `cwi-admin-console`, with `cwi-identity-admin` as an adjunct auth-admin surface.
- `R3` Approvals surface is already covered by `cwi-teams-bot` plus the browser approvals path in `cwi-glassbox-web-ui`.
- `R4` Gateway / ingress is already covered by `cwi-gateway`.
- `R5` API router is already covered by `cwi-router`.
- `R7` Pack distribution is already covered by the existing `cwi-pack-service`
  plus `cwi-pin-resolver` governance bridge.
- `R8` Teacher publishing / graph build is already covered by a composite of `teacher-node` and `cwi-etl`.
- `I1` through `I7` are covered by existing graph, vector, model, state, cache, identity, and artifact-custody assets.

### Partial roles that still need work

- `R1` User-facing Q&A UI is present and now renders graph-aware trace fields in the repo, but live validation is still pending.
  - Action: run frontend type/build/tests and verify a live graph-backed ask path.
- `R6` Orchestrator is present and now has graph config, AGE retrieval, planner, prompt merge, and graph-aware receipt fields in the current worktree, but live validation is still pending.
  - Action: run .NET 8 tests, wire graph env vars in deployment, and verify live graph-backed asks.
- `T2` Graph build utility set is now implemented as repo-native ETL modules with thin teacher-node wrappers.
  - Action: keep graph tooling consolidated under ETL/teacher tooling and validate the teacher-node image path in deployed environments.

## True Gaps

These are the only true implementation or packaging gaps surfaced by this pass.

### `cwi-user-web` is not a current application role

- `src/frontend/web-ui/cwi-user-web` exists as an empty placeholder directory in this pass.
- There is no code, no package manifest, and no deployment evidence.
- This should not be auto-created or counted as a missing required app unless a specific functional role is assigned to it first.

## Explicit Non-Gaps

These items looked like gaps in handoff material, but repo evidence shows they are not new missing apps.

### No second orchestrator should be created

- The correct move is to patch `cwi-orchestrator`, not create a second orchestration service.

### No duplicate admin/operator UI should be created just to satisfy naming drift

- The repo already splits the operator surface across `cwi-glassbox-web-ui` and `cwi-admin-console`.
- That is a valid composite satisfaction of the role.

### The eight graph-script names are not separate deployable apps

- `cwi_graph_ontology.py`
- `cwi_entity_extractor.py`
- `cwi_relationship_extractor.py`
- `cwi_graph_backfill.py`
- `cwi_graph_rollup.py`
- `cwi_graph_curate.py`
- `cwi_graph_crosswalk.py`
- `cwi_age_graph_writer.py`

Those names now exist as thin teacher-node entrypoints over repo-native ETL modules. They should still be treated as tooling wrappers, not as separate runtime applications or microservices.

### The graph database is already accounted for

- The current repo and compose files use `graphdb` / `cwi-graph-db`.
- A separate `cwi-age-db` application does not need to be invented.

## Phase-Gated Roles

These roles exist or are partially present, but they are not blockers for the current server-side graph-runtime objective.

- `P1` iOS + CarPlay client: source present.
- `P2` Android + Android Auto client: source present.
- `P3` Edge telemetry agent: source and DEPLOY bundle present.
- `P4` ETL / consent ingest: source and current deployment presence confirmed.
- `P5` Enterprise connectors: source and DEPLOY bundle present.
- `P6` Coordination layer: source and deployment presence confirmed.
- `P7` Node agent runtime: source, Dockerfile, and DEPLOY bundle are now present.

## Recommended Next Actions

1. Treat the current graph-runtime blocker as an integration-validation problem, not a missing-app problem.
2. Validate the existing `cwi-orchestrator` graph path with .NET 8 available.
3. Validate the existing `cwi-gpt-web-ui` graph-aware rendering with Node available.
4. Keep the teacher-side graph utilities classified as tooling unless a concrete runtime need is proven.
5. Decide whether `cwi-node-agent-runtime` should be enabled in the target environment now that packaging exists.
