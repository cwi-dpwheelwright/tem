# Component Role Inventory

Date: 2026-03-20
Status: Working inventory based on repo and manifest evidence

This inventory maps CWI by functional role, not by exact app-name string. It uses the precedence described in `/Users/astrion9/Documents/CWI_Codex_Functional_App_Alignment_Spec.md`, but grounds each role in this repository's actual code and deployment manifests.

Primary evidence used:
- `docs/ops/cwi-system-architecture-alignment-spec-2026-02-27.md`
- `docs/architecture/application-architecture.md`
- `docs/ops/current-application-stack.md`
- `OPS/cwi-ops/compose/`
- `OPS/cwi-ops/DEPLOY/`
- `src/`
- `tools/`

Status semantics:
- `satisfied`: one component satisfies the role
- `composite-satisfied`: multiple existing components jointly satisfy the role
- `partial`: role exists, but validation, packaging, or feature completeness is still incomplete
- `missing`: no repo/manifests evidence that the role exists
- `unknown`: evidence is too weak to classify safely

## Runtime Roles

| Role ID | Functional role | Observed component(s) | Evidence | Status | Notes |
| --- | --- | --- | --- | --- | --- |
| `R1` | User-facing Q&A UI | `cwi-gpt-web-ui` | `src/frontend/web-ui/cwi-gpt-web-ui`; `OPS/cwi-ops/compose/cwi-gpt-web-ui/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/cwi-gpt-web-ui/docker-compose.yml` | `partial` | Repo now renders answer, sources, confidence, and graph-aware trace fields. Live graph-backed validation is still pending. |
| `R2` | Admin / glassbox / operator UI | `cwi-glassbox-web-ui`, `cwi-admin-console`, `cwi-identity-admin` | `src/frontend/web-ui/cwi-glassbox-web-ui`; `src/frontend/web-ui/cwi-admin-console`; `src/frontend/web-ui/cwi-identity-admin`; matching compose and DEPLOY bundles | `composite-satisfied` | Glassbox covers approvals, trace, kill-switch, audit. Admin console covers pack catalog, upload, retire/delete, audit log. Identity admin is a support/admin adjunct. |
| `R3` | Approvals surface | `cwi-teams-bot`, `cwi-glassbox-web-ui` | `src/backend/services/cwi-teams-bot`; `src/frontend/web-ui/cwi-glassbox-web-ui/src/components/approvals-inbox.tsx`; `OPS/cwi-ops/DEPLOY/cwi-teams-bot/docker-compose.yml` | `composite-satisfied` | Teams bot is the enterprise approvals channel; Glassbox provides a browser-based approval completion path. |
| `R4` | Gateway / ingress | `cwi-gateway` | `docs/architecture/application-architecture.md`; `OPS/cwi-ops/compose/gateway/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/gateway/docker-compose.yml` | `satisfied` | Public ingress for `/api/*` and `/packs/*`. |
| `R5` | API router / control plane | `cwi-router` | `src/backend/services/cwi-router`; `OPS/cwi-ops/compose/cwi-router/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml` | `satisfied` | Current stack and manifests consistently place the router in the core bundle. |
| `R6` | Orchestrator / workflow engine | `cwi-orchestrator` | `src/backend/services/cwi-orchestrator`; `OPS/cwi-ops/compose/cwi-orchestrator/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/cwi-orchestrator/docker-compose.yml` | `partial` | Core workflow engine exists and pack-contract compatibility work is present. Graph query planning, AGE retrieval, prompt merge, and graph-aware trace fields are implemented in the current worktree but not yet runtime-validated on this machine. |
| `R7` | Pack distribution service | `cwi-pack-service`, `cwi-pin-resolver` | `src/backend/services/cwi-pack-service`; `src/backend/services/cwi-pin-resolver`; `OPS/cwi-ops/compose/pack-service/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/pack-service/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml` | `composite-satisfied` | `cwi-pack-service` serves pack artifacts and listings. `cwi-pin-resolver` adds governed signed-pin resolution and legacy-compatible `latest` pack routing for MCP consumers. |
| `R8` | Teacher publishing / graph build host | `teacher-node`, `cwi-etl` | `tools/teacher-node`; `tools/teacher-node/graph_cli.py`; `src/etl`; `src/etl/cwi_etl/cwi_entity_extractor.py`; `src/etl/cwi_etl/cwi_graph_crosswalk.py`; `OPS/cwi-ops/compose/teacher_node/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/teacher_node/docker-compose.yml` | `composite-satisfied` | Teacher-node owns operational entrypoints. ETL now also includes repo-native ontology, extraction, rollup, curation, crosswalk, backfill, and AGE artifact generation utilities with thin teacher-side wrappers. |

## Infrastructure Roles

| Role ID | Functional role | Observed component(s) | Evidence | Status | Notes |
| --- | --- | --- | --- | --- | --- |
| `I1` | Graph database | `graphdb`, `cwi-graph-db`, retained teacher-host `cwi-age-db` | `docs/ops/current-application-stack.md`; `OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/graph_db/docker-compose.yml`; `OPS/cwi-ops/compose/full/docker-compose.full.yml`; `src/etl/README.md` | `satisfied` | MCP remains the core graph DB runtime, and a separate teacher-host `cwi-age-db` deployment is retained for teacher-side graph workflows. |
| `I2` | Vector evidence store | Qdrant shard(s) | `docs/ops/current-application-stack.md`; `OPS/cwi-ops/compose/vector_shard/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/vector_shard/docker-compose.yml` | `satisfied` | Current stack describes two vector shards; dedicated vector shard bundle exists. |
| `I3` | Model inference endpoint(s) | Ollama model workers plus health probes | `OPS/cwi-ops/compose/gpu_worker/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/gpu_worker/docker-compose.yml` | `satisfied` | Repo/manifests show real inference containers in addition to health probe containers. |
| `I4` | Durable state store | Postgres | `docs/architecture/application-architecture.md`; `OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml`; identity and full-stack compose bundles | `satisfied` | Used across identity, orchestrator, and related durable state. |
| `I5` | Cache / locks / rate-limit store | Redis | `docs/ops/current-application-stack.md`; `OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml`; `OPS/cwi-ops/compose/full/docker-compose.full.yml` | `satisfied` | Used by router, bot, and other coordination/rate-limiting paths. |
| `I6` | Identity / auth support | `cwi-identity-server`, `cwi-identity-admin` | `src/backend/services/cwi-identity-server`; `src/frontend/web-ui/cwi-identity-admin`; `OPS/cwi-ops/compose/cwi-identity-stack/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/cwi-identity-admin/docker-compose.yml` | `satisfied` | Present in the current deployed stack and repo. |
| `I7` | Artifact / object custody | Filesystem-backed teacher artifact roots plus pack artifact roots | `docs/architecture/application-architecture.md`; `OPS/cwi-ops/compose/teacher_node/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/teacher_node/docker-compose.yml`; `OPS/cwi-ops/runbooks/pack-lifecycle.md` | `composite-satisfied` | The current target deployment keeps teacher artifacts filesystem-backed. An optional teacher MinIO bundle remains available but is not part of the default rollout model. |

## Tooling / Release Control Roles

| Role ID | Functional role | Observed component(s) | Evidence | Status | Notes |
| --- | --- | --- | --- | --- | --- |
| `T1` | Gold tests / regression harness | `cwi-gold-tests` | `tools/cwi-gold-tests`; `tools/cwi-gold-tests/README.md`; `tools/cwi-gold-tests/tests/` | `satisfied` | Gold-test coverage exists for pack lifecycle, trace, graph contracts, kill-switch, ETL, teams bot, vector shard, and more. Local execution evidence is still pending on this machine. |
| `T2` | Graph build utility set | repo-native ETL graph tooling plus teacher-node wrappers | `src/etl/cwi_etl/graph_ontology.py`; `src/etl/cwi_etl/cwi_entity_extractor.py`; `src/etl/cwi_etl/cwi_relationship_extractor.py`; `src/etl/cwi_etl/cwi_graph_rollup.py`; `src/etl/cwi_etl/cwi_graph_curate.py`; `src/etl/cwi_etl/cwi_graph_crosswalk.py`; `src/etl/cwi_etl/cwi_age_graph_writer.py`; `tools/teacher-node/graph_cli.py` | `satisfied` | The graph preparation layer now exists as repo-native modules with thin CLI entrypoints. These wrappers are tooling, not separate deployable apps. |
| `T3` | Ops / observability | `OPS/cwi-ops`, observability configs, runbooks | `OPS/cwi-ops/README.md`; `OPS/cwi-ops/observability/`; `OPS/cwi-ops/runbooks/` | `satisfied` | Compose, deploy, runbook, and observability assets exist in the repo. |

## 10H Parity / Phase-Gated Roles

| Role ID | Functional role | Observed component(s) | Evidence | Status | Notes |
| --- | --- | --- | --- | --- | --- |
| `P1` | iOS + CarPlay client | iOS/CarPlay app | `src/mobile/ios-carplay`; Xcode project, source, tests | `satisfied` | Source exists and includes tests. This is not part of the current server-side graph blocker. |
| `P2` | Android + Android Auto client | Android app | `src/mobile/android`; Gradle project and docs | `satisfied` | Source exists. Deployment/release flow was not audited in this pass. |
| `P3` | Edge telemetry agent | `cwi-edge-agent` | `src/edge-agent`; `src/edge-agent/Dockerfile`; `OPS/cwi-ops/DEPLOY/edge_agent/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml` | `satisfied` | Present and deployment-ready. It is now also included in the MCP target deployment model. |
| `P4` | ETL / document ingest pipeline | `cwi-etl`, `cwi-consent-ingest` | `src/etl`; `src/etl/docker/Dockerfile`; `OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml` | `satisfied` | Current deployment already includes `cwi-consent-ingest` as the ETL job. |
| `P5` | Enterprise connectors | `cwi-connectors` | `src/connectors`; `src/connectors/docker/Dockerfile`; `OPS/cwi-ops/DEPLOY/connectors/docker-compose.yml` | `satisfied` | Optional by deployment phase, but present and deployment-ready. |
| `P6` | Coordination layer | `cwi-coordination-layer` | `src/backend/services/cwi-coordination-layer`; `OPS/cwi-ops/compose/cwi-coordination-layer/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml` | `satisfied` | Present in the current internal stack and baseline docs. |
| `P7` | Node agent runtime | `cwi-node-agent-runtime` | `src/node-agent-runtime`; `src/node-agent-runtime/Dockerfile`; `OPS/cwi-ops/DEPLOY/node_agent_runtime/docker-compose.yml`; `OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml`; `OPS/cwi-ops/runbooks/node-agent-runtime.md` | `satisfied` | Source package, Dockerfile, deploy bundle, and runbook now exist. It is now also included in the MCP target deployment model. |

## Unmapped or Alias-Only Findings

- `cwi-node-ui-web` is not a repo path in this codebase. Its responsibilities map more closely to the existing `cwi-glassbox-web-ui` plus `cwi-admin-console` split.
- `cwi-age-db` now exists as a retained teacher-host deployment bundle under `OPS/cwi-ops/DEPLOY/graph_db`, distinct from the MCP-host `cwi-graph-db`.
- The eight graph-script names from the handoff (`cwi_graph_ontology.py`, `cwi_entity_extractor.py`, and related names) now exist as thin teacher-node wrappers over repo-native ETL modules. They should still be treated as tooling entrypoints, not as separate deployable applications.
- `src/frontend/web-ui/cwi-user-web` exists only as an empty placeholder directory in this pass and is not mapped to a required role.
