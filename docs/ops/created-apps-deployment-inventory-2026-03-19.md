# Created Apps Deployment Inventory — 2026-03-19

This inventory was produced by reviewing:

- `/Users/astrion9/Documents/CWI_Codex_Backlog_Handoff_2026-03-19.md`
- `docs/ops/current-application-stack.md`
- `docs/architecture/application-architecture.md`
- iteration and deployment docs under `docs/platform/iteration-*`
- compose and deploy bundles under `OPS/cwi-ops/compose/` and `OPS/cwi-ops/DEPLOY/`

## What this inventory answers

1. Which apps already exist in the repo.
2. Which apps are already represented in deployment bundles.
3. Which optional or scope-dependent apps were created but had no `DEPLOY/` coverage.
4. Which apps still exist only as source/runtime skeletons and are not yet packaging-complete.

## Result

The following created apps were missing `DEPLOY/` bundle coverage before this change set and are now explicitly represented:

- `cwi-gateway`
- `cwi-gpt-web-ui`
- `cwi-glassbox-web-ui`
- `cwi-homepage-web-ui`
- `cwi-admin-console`
- `cwi-identity-admin` (legacy/transitional)
- `cwi-connectors`
- `cwi-edge-agent`

## Clarification on the graph script names from the handoff

The March 19 handoff listed the following as if they were existing graph pipeline components:

- `cwi_graph_ontology.py`
- `cwi_entity_extractor.py`
- `cwi_relationship_extractor.py`
- `cwi_graph_backfill.py`
- `cwi_graph_rollup.py`
- `cwi_graph_curate.py`
- `cwi_graph_crosswalk.py`
- `cwi_age_graph_writer.py`

Important clarification:

- these are not deployable apps in this repo,
- they should not be added to `DEPLOY/` as standalone services.

What exists in the current repo instead:

- graph projection logic in [graph_rag.py](/Users/astrion9/git/cwi-platform/src/etl/cwi_etl/graph_rag.py)
- graph storage / AGE writing in [graph_store.py](/Users/astrion9/git/cwi-platform/src/etl/cwi_etl/graph_store.py)
- orchestration hooks in [pipeline.py](/Users/astrion9/git/cwi-platform/src/etl/cwi_etl/pipeline.py)
- repo-native graph tooling modules and thin teacher-node wrappers including:
  - [graph_ontology.py](/Users/astrion9/git/cwi-platform/src/etl/cwi_etl/graph_ontology.py)
  - [cwi_entity_extractor.py](/Users/astrion9/git/cwi-platform/tools/teacher-node/cwi_entity_extractor.py)
  - [cwi_relationship_extractor.py](/Users/astrion9/git/cwi-platform/tools/teacher-node/cwi_relationship_extractor.py)
  - [cwi_graph_rollup.py](/Users/astrion9/git/cwi-platform/tools/teacher-node/cwi_graph_rollup.py)
  - [cwi_graph_curate.py](/Users/astrion9/git/cwi-platform/tools/teacher-node/cwi_graph_curate.py)
  - [cwi_graph_crosswalk.py](/Users/astrion9/git/cwi-platform/tools/teacher-node/cwi_graph_crosswalk.py)
  - [cwi_age_graph_writer.py](/Users/astrion9/git/cwi-platform/tools/teacher-node/cwi_age_graph_writer.py)
  - [cwi_graph_backfill.py](/Users/astrion9/git/cwi-platform/tools/teacher-node/cwi_graph_backfill.py)
- graph tests in:
  - [test_graph_rag.py](/Users/astrion9/git/cwi-platform/src/etl/tests/test_graph_rag.py)
  - [test_graph_store.py](/Users/astrion9/git/cwi-platform/src/etl/tests/test_graph_store.py)
  - [test_graph_store_integration.py](/Users/astrion9/git/cwi-platform/src/etl/tests/test_graph_store_integration.py)

Interpretation:

- the handoff appears to be referring to a teacher-node or prior-environment script set that is not checked into this repo under those names,
- in this repo, the nearest current implementation lives under `src/etl/cwi_etl/`,
- these should be treated as graph-pipeline functionality, not as missing deployable apps.

## Inventory

| App / service | Repo presence | Container-ready | Deployment status before review | Status after review |
| --- | --- | --- | --- | --- |
| `cwi-gateway` | Present via ops bundle | Yes | Compose-only, no `DEPLOY/` bundle | `DEPLOY/gateway` added |
| `cwi-gpt-web-ui` | Present | Yes | Compose-only, no `DEPLOY/` bundle | `DEPLOY/cwi-gpt-web-ui` added |
| `cwi-glassbox-web-ui` | Present | Yes | Compose-only, no `DEPLOY/` bundle | `DEPLOY/cwi-glassbox-web-ui` added |
| `cwi-homepage-web-ui` | Present | Yes | Compose-only, no `DEPLOY/` bundle | `DEPLOY/cwi-homepage-web-ui` added |
| `cwi-admin-console` | Present | Yes | Only present in full local compose | `DEPLOY/cwi-admin-console` added |
| `cwi-identity-admin` | Present | Yes | Compose-only, legacy but still deployable | `DEPLOY/cwi-identity-admin` added |
| `cwi-router` | Present | Yes | Already deployed in `mcp-core` | No gap |
| `cwi-orchestrator` | Present | Yes | Already deployed in `mcp-core` / dedicated bundle | No gap |
| `cwi-coordination-layer` | Present | Yes | Already deployed in `mcp-core` | No gap |
| `cwi-identity-server` | Present | Yes | Already deployed in `mcp-core` | No gap |
| `cwi-pack-service` | Present | Yes | Already deployed | No gap |
| `cwi-teams-bot` | Present | Yes | Already deployed in `mcp-core` / dedicated bundle | No gap |
| `teacher-node` | Present | Yes | Already deployed in teacher-node bundles | No gap |
| `teacher-minio` | Present as teacher-node adjunct | Yes | Already deployed as optional teacher profile | No gap |
| `cwi-etl` | Present | Yes | Deployed as `cwi-consent-ingest`; no standalone service bundle | Partial, but deployment path exists |
| `cwi-consent-ingest` | Present via ETL image/job | Yes | Already deployed in `mcp-core` | No gap |
| `cwi-connectors` | Present | Yes | No `DEPLOY/` bundle | `DEPLOY/connectors` added |
| `cwi-edge-agent` | Present | Yes | No `DEPLOY/` bundle | `DEPLOY/edge_agent` added |
| `cwi-node-agent-runtime` | Present | Yes | No `DEPLOY/` bundle | `DEPLOY/node_agent_runtime` added |
| `cwi-user-web` | Directory placeholder only | No | No `DEPLOY/` bundle | Still a packaging gap |
| handoff graph script names (`cwi_graph_ontology.py`, etc.) | Not present under those names | No | Not deployable apps | Classified as stale/non-repo script references; mapped to `src/etl/cwi_etl/*` functionality where applicable |

## Interpretation of “optional”

The backlog handoff was correct that some baseline apps are only optional in the generic 10H sense. In this repo, those apps still exist and serve a purpose, so they should not be treated as disposable:

- `cwi-connectors`: enterprise/workspace ingestion boundary
- `cwi-edge-agent`: telemetry/OPC-UA/buffered ingest boundary
- `cwi-etl`: ingestion, graph projection, consent ingest, provenance support
- `cwi-homepage-web-ui`: public landing/beta access surface
- `cwi-glassbox-web-ui`: alternate/operator-facing UI surface
- `cwi-admin-console`: consolidated admin surface
- `cwi-identity-admin`: legacy admin UI, still useful during cutover

## Remaining gaps after this review

These still need packaging decisions, not just deployment manifests:

1. `cwi-user-web`
   - directory exists, but no implementation/deployable package is present

## Files adjusted as part of this review

- `OPS/cwi-ops/README.md`
- `OPS/ENV_VAR_MATRIX.md`
- `OPS/cwi-ops/compose/full/docker-compose.full.yml`
- `OPS/cwi-ops/compose/full/README.md`
- `docs/ops/current-application-stack.md`
- `OPS/cwi-ops/DEPLOY/gateway/docker-compose.yml`
- `OPS/cwi-ops/DEPLOY/cwi-gpt-web-ui/docker-compose.yml`
- `OPS/cwi-ops/DEPLOY/cwi-glassbox-web-ui/docker-compose.yml`
- `OPS/cwi-ops/DEPLOY/cwi-homepage-web-ui/docker-compose.yml`
- `OPS/cwi-ops/DEPLOY/cwi-admin-console/docker-compose.yml`
- `OPS/cwi-ops/DEPLOY/cwi-identity-admin/docker-compose.yml`
- `OPS/cwi-ops/DEPLOY/connectors/docker-compose.yml`
- `OPS/cwi-ops/DEPLOY/connectors/connectors.yaml`
- `OPS/cwi-ops/DEPLOY/edge_agent/docker-compose.yml`
- `OPS/cwi-ops/DEPLOY/edge_agent/edge-agent.yaml`
- `OPS/cwi-ops/DEPLOY/node_agent_runtime/docker-compose.yml`
- `OPS/cwi-ops/DEPLOY/node_agent_runtime/node-agent.yaml`

## Recommendation

Treat `cwi-connectors`, `cwi-edge-agent`, `cwi-etl`, `cwi-homepage-web-ui`, `cwi-glassbox-web-ui`, and `cwi-admin-console` as deployment-scope components, not “nice-to-have leftovers.” They are now represented in deployment bundles, so the remaining decision is whether each should be enabled for the target environment, not whether they exist.
