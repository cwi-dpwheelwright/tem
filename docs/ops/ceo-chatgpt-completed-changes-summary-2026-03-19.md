# CEO / ChatGPT Completed Changes Summary — 2026-03-19

Prepared for: CEO and delegated ChatGPT review  
Prepared by: Codex  
Scope covered: backlog review, GraphRAG orchestration work, deployment coverage review, and deployment bundle additions

## Executive Summary

The repo and deployment assets were reviewed against the March 19 backlog handoff and the current iteration/architecture docs. Two major categories of work were completed:

1. The first GraphRAG integration slice was implemented inside `cwi-orchestrator` and exposed through the GPT UI trace path.
2. A deployment inventory review was completed, and multiple created-but-not-deployed scope-dependent apps were given explicit `DEPLOY/` bundle coverage.

The key business outcome is that the repo is now materially closer to the intended architecture in two ways:

- the orchestrator can now begin consuming graph evidence instead of remaining vector-only,
- the ops/deployment layer now explicitly represents several created applications that previously existed in source or local compose only.

## What Was Completed

### A. GraphRAG integration work completed in the orchestrator

The orchestrator now has a first-pass graph-aware ask path.

Completed capabilities:

- added graph config support through API config, composition, and application options,
- added an Apache AGE adapter using `Npgsql`,
- added graph query planning for an initial template set,
- inserted graph retrieval into the ask workflow before vector retrieval,
- updated prompt construction to distinguish graph evidence from document evidence,
- updated response trace payloads to include graph-aware metadata,
- updated the GPT UI trace display for graph/document evidence and reasoning receipt fields.

Initial graph query coverage added:

- equipment and metrics,
- maintenance attention,
- procedure-family relationships.

Trace/response additions completed:

- `graph_hits`
- `document_hits`
- `reasoning_receipt`

This work is summarized in detail here:

- [graph-rag-integration-change-summary-2026-03-19.md](/Users/astrion9/git/cwi-platform/docs/orchestrator/graph-rag-integration-change-summary-2026-03-19.md)

### B. GPT UI routing behavior was corrected

The GPT web UI previously had a silent direct-to-LLM shortcut that could bypass the orchestrator whenever `CWI_LLM_*` variables were present.

That behavior is now explicit opt-in.

Completed change:

- direct UI-to-LLM ask mode now requires `CWI_UI_DIRECT_LLM_ENABLED=true`,
- otherwise the default ask path goes through the gateway and orchestrator.

Business impact:

- graph-backed orchestration work is no longer silently bypassed by local UI configuration,
- deployments are less likely to appear “working” while actually skipping the orchestrator pipeline.

### C. Unit test coverage was expanded for the new graph path

Tests were added or updated for:

- prompt generation with graph and document evidence,
- graph retrieval step success/failure handling,
- graph-aware response shaping,
- application option constructor updates.

This does not mean tests were executed successfully on this machine.

## Deployment Review Completed

### D. A full created-app inventory was completed

The backlog and iteration materials were reviewed against:

- actual repo applications,
- current architecture docs,
- compose bundles,
- deploy bundles.

This identified which apps:

- already exist in repo,
- are already deployed,
- exist but only had local compose coverage,
- exist but still lack packaging/containerization.

Inventory document created:

- [created-apps-deployment-inventory-2026-03-19.md](/Users/astrion9/git/cwi-platform/docs/ops/created-apps-deployment-inventory-2026-03-19.md)

### E. Created apps that were missing deploy-bundle coverage were added

The following created, scope-dependent, or public-surface apps were given explicit `DEPLOY/` coverage:

- `cwi-gateway`
- `cwi-gpt-web-ui`
- `cwi-glassbox-web-ui`
- `cwi-homepage-web-ui`
- `cwi-admin-console`
- `cwi-identity-admin`
- `cwi-connectors`
- `cwi-edge-agent`

New deploy bundle files added:

- [OPS/cwi-ops/DEPLOY/gateway/docker-compose.yml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/gateway/docker-compose.yml)
- [OPS/cwi-ops/DEPLOY/cwi-gpt-web-ui/docker-compose.yml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/cwi-gpt-web-ui/docker-compose.yml)
- [OPS/cwi-ops/DEPLOY/cwi-glassbox-web-ui/docker-compose.yml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/cwi-glassbox-web-ui/docker-compose.yml)
- [OPS/cwi-ops/DEPLOY/cwi-homepage-web-ui/docker-compose.yml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/cwi-homepage-web-ui/docker-compose.yml)
- [OPS/cwi-ops/DEPLOY/cwi-admin-console/docker-compose.yml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/cwi-admin-console/docker-compose.yml)
- [OPS/cwi-ops/DEPLOY/cwi-identity-admin/docker-compose.yml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/cwi-identity-admin/docker-compose.yml)
- [OPS/cwi-ops/DEPLOY/connectors/docker-compose.yml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/connectors/docker-compose.yml)
- [OPS/cwi-ops/DEPLOY/edge_agent/docker-compose.yml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/edge_agent/docker-compose.yml)

Deployment config stubs added for optional Python runtimes:

- [OPS/cwi-ops/DEPLOY/connectors/connectors.yaml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/connectors/connectors.yaml)
- [OPS/cwi-ops/DEPLOY/edge_agent/edge-agent.yaml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/edge_agent/edge-agent.yaml)

### F. Ops docs and bundle indexes were updated

To prevent these apps from being treated as “not real” or “not part of deployment,” the following documentation and bundle references were updated:

- [OPS/cwi-ops/README.md](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/README.md)
- [OPS/ENV_VAR_MATRIX.md](/Users/astrion9/git/cwi-platform/OPS/ENV_VAR_MATRIX.md)
- [OPS/cwi-ops/DEPLOY/mcp-core/README.md](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/mcp-core/README.md)
- [OPS/cwi-ops/compose/full/docker-compose.full.yml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/compose/full/docker-compose.full.yml)
- [OPS/cwi-ops/compose/full/README.md](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/compose/full/README.md)
- [docs/ops/current-application-stack.md](/Users/astrion9/git/cwi-platform/docs/ops/current-application-stack.md)

## Created Apps Now Explicitly Accounted For

These created apps are now explicitly represented as deployable or scope-dependent parts of the system:

- `cwi-gateway`
- `cwi-gpt-web-ui`
- `cwi-glassbox-web-ui`
- `cwi-homepage-web-ui`
- `cwi-admin-console`
- `cwi-identity-admin`
- `cwi-router`
- `cwi-orchestrator`
- `cwi-coordination-layer`
- `cwi-identity-server`
- `cwi-pack-service`
- `cwi-teams-bot`
- `teacher-node`
- `teacher-minio`
- `cwi-etl`
- `cwi-consent-ingest`
- `cwi-connectors`
- `cwi-edge-agent`

## Clarification on the graph script names in the handoff

The backlog handoff also named these items:

- `cwi_graph_ontology.py`
- `cwi_entity_extractor.py`
- `cwi_relationship_extractor.py`
- `cwi_graph_backfill.py`
- `cwi_graph_rollup.py`
- `cwi_graph_curate.py`
- `cwi_graph_crosswalk.py`
- `cwi_age_graph_writer.py`

These are not deployable apps in the current repo.

What review found:

- those exact file names do not exist in this repo,
- they should not be treated as missing microservices,
- the nearest current graph implementation in this repo is consolidated under:
  - [graph_rag.py](/Users/astrion9/git/cwi-platform/src/etl/cwi_etl/graph_rag.py)
  - [graph_store.py](/Users/astrion9/git/cwi-platform/src/etl/cwi_etl/graph_store.py)
  - [pipeline.py](/Users/astrion9/git/cwi-platform/src/etl/cwi_etl/pipeline.py)

Leadership implication:

- this was a naming / provenance mismatch in the handoff,
- not evidence that eight additional deployable “apps” were forgotten.

## Important Clarification on “Optional” Apps

One of the most important outcomes of this review is that “optional” does not mean “unnecessary” or “mistakenly created.”

In this repo, several apps are optional only in the generic baseline sense. They still clearly serve defined purposes and belong in the deployment model when the deployment scope requires them.

Examples:

- `cwi-connectors`: external enterprise/workspace ingestion boundary
- `cwi-edge-agent`: telemetry and buffered ingest boundary
- `cwi-etl`: ingestion, graph projection, consent ingest, provenance support
- `cwi-homepage-web-ui`: public landing/beta access surface
- `cwi-glassbox-web-ui`: alternate/operator-facing UI
- `cwi-admin-console`: consolidated admin surface
- `cwi-identity-admin`: legacy transitional admin UI during cutover

## Remaining Gaps

Two repo apps still require packaging work before they can honestly be called deployment-ready:

1. `cwi-node-agent-runtime`
   - source/runtime exists,
   - no Dockerfile or deploy bundle exists yet.

2. `cwi-user-web`
   - listed as a web-ui directory in the repo tree context,
   - no actual deployable app/package path was found during this review.

These are not deployment-bundle omissions. They are packaging/completeness gaps.

## Validation Limits

The changes above were completed in source and deployment manifests, but there are still execution-environment limits on this machine:

- `.NET SDK 8.x` is not installed here, while the repo is pinned to `.NET 8.0.100`,
- `node` and `npm` are not installed here,
- deployment bundles were not brought up with live `docker compose up`,
- environment-specific `.env` values and secrets were not available for a real deployment test.

Meaning:

- the engineering and ops updates are in place,
- live validation still requires a proper runtime/deployment host.

## Practical Outcome for Leadership

From a CEO-level perspective, the completed work did three things:

1. It reduced architectural drift.
   - The orchestrator now has a real path toward graph-backed reasoning instead of remaining structurally vector-only.

2. It reduced deployment ambiguity.
   - Multiple created apps that previously existed only as source or local compose artifacts now have explicit deployment bundle coverage.

3. It clarified the difference between:
   - deployed core services,
   - deployment-scope adjunct services,
   - source-only or not-yet-packaged runtimes.

That means the team now has a much clearer answer to:

- what has actually been built,
- what is now deployment-accounted-for,
- what still needs packaging,
- what still needs live validation.

## Recommended Next Step

Run this in a proper validation environment with:

- `.NET 8.x`
- Node/npm
- real `.env` values and secrets
- Docker compose access
- access to the intended target hosts

Then:

1. execute the orchestrator and UI build/test loops,
2. validate the new deploy bundles,
3. decide which scope-dependent apps should be enabled for the target environment,
4. package `cwi-node-agent-runtime` if that runtime is intended to be part of the shipped deployment.
