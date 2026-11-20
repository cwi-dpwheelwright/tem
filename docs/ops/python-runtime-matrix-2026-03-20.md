# Python Runtime Matrix — 2026-03-20

Prepared for: Demitrius / Codex / ChatGPT / leadership review  
Purpose: Clarify what each supported Python component is supposed to do, where it is supposed to run, when it is supposed to run, and what is still an operational decision rather than a missing implementation.

## Executive Summary

The repo is now clear enough to distinguish between:

- Python packages/utilities,
- always-on Python runtimes,
- scheduled/background Python jobs,
- and teacher-side/manual Python tooling.

The biggest clarification is this:

- `cwi-etl` is a Python package and image, but not a single always-on “ETL daemon” by itself.
- The currently deployed always-on ETL job is `cwi-consent-ingest`.
- The teacher-side graph ETL functions run through `teacher-node` wrappers over `cwi-etl`.
- The current teacher deployment model uses a single internal `teacher-node` profile, with the separate teacher-host `cwi-age-db` retained alongside it.
- `cwi-connectors`, `cwi-edge-agent`, and `cwi-node-agent-runtime` are real Python runtimes, but their environment-by-environment enablement is still a deployment decision.

## Current Clarity Verdict

### Clear now

- what each supported Python component is for,
- whether it is package/library style or runtime/agent style,
- the main entrypoint or command shape,
- the expected host class for each component,
- the immediate runtime dependencies for each component.

### Not fully settled yet

- whether all optional Python runtimes should be enabled in every target environment,
- whether `cwi-etl` should remain split across `cwi-consent-ingest` and teacher workflows or gain a dedicated standalone service bundle,
- when teacher-side graph jobs should run in production: manual, CI, scheduled, or release-pipeline driven.

## Python Runtime Matrix

| Component | What it does | Primary entrypoint | Run model | Where it is supposed to run | Immediate dependencies | Current deployment state | Clarity level |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `cwi-etl` | Shared ETL/utilities package for parsing, normalization, consent handling, graph projection, graph artifact generation | [__main__.py](/Users/astrion9/git/cwi-platform/src/etl/cwi_etl/__main__.py), [pipeline.py](/Users/astrion9/git/cwi-platform/src/etl/cwi_etl/pipeline.py) | Library/module plus image, not a standalone always-on daemon by default | Internal build/runtime contexts, especially MCP and teacher workflows | Python 3.10+, ETL image, graph DB for graph flows, consent store inputs for consent flows | Packaged and used indirectly | `clear on code role`, `not fully settled as standalone runtime role` |
| `cwi-consent-ingest` | Continuous consent ingestion into the consent store for operator-memory enforcement | [mcp-core/docker-compose.yml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml), [consent-ingest.py](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/mcp-core/consent-ingest.py) | Always-on background ETL job | `internal-mcp-01` / MCP bundle | `cwi-etl` image, input/archive/error/store volumes, optional remote consent endpoint | Deployed in MCP manifests and full compose | `clear` |
| `teacher-node` | Pack validation, publish/rollback, reports, reconciliation, graph-prep operator entrypoints | [teacher_node.py](/Users/astrion9/git/cwi-platform/tools/teacher-node/teacher_node.py), [graph_cli.py](/Users/astrion9/git/cwi-platform/tools/teacher-node/graph_cli.py) | Manual, CI-driven, operator-driven, and containerized service for teacher workflows | `internal-teacher-01` or teacher tooling host | filesystem artifact store, `cwi-etl`, teacher-host `cwi-age-db`, optional orchestrator/provenance endpoints | Deployed and now packaged with ETL graph tooling | `clear on role`, `schedule policy still needs explicit decision` |
| teacher-side graph tooling via `cwi-etl` + wrappers | Ontology, entity extraction, relationship extraction, rollup, curation, crosswalk, AGE artifact generation, backfill | [graph_ontology.py](/Users/astrion9/git/cwi-platform/src/etl/cwi_etl/graph_ontology.py), [cwi_entity_extractor.py](/Users/astrion9/git/cwi-platform/tools/teacher-node/cwi_entity_extractor.py), [cwi_age_graph_writer.py](/Users/astrion9/git/cwi-platform/tools/teacher-node/cwi_age_graph_writer.py) | Manual/CI/scheduled teacher pipeline tasks | Teacher/build side, not user ask runtime | `teacher-node`, `cwi-etl`, graph ontology/config, graph DB for load/verify | Implemented and packaged through teacher-node | `clear on implementation`, `not fully settled on production scheduling` |
| `cwi-connectors` | Enterprise/workspace polling, ingestion handoff, reconciliation/admin hooks | [__main__.py](/Users/astrion9/git/cwi-platform/src/connectors/cwi_connectors/__main__.py) | Always-on polling runtime, with optional `--once` mode | Internal integrations host or internal service tier | connectors YAML, local state, gateway/orchestrator/reconciliation endpoints, auth tokens | Deployment-ready and included in full-stack deployment assets | `clear on runtime shape`, `enablement is deployment-specific` |
| `cwi-edge-agent` | Edge telemetry collection, buffering, ingest retry, survival signals, metrics export | [cli.py](/Users/astrion9/git/cwi-platform/src/edge-agent/edge_agent/cli.py) | Always-on client/edge agent | Client-restricted edge/customer node | edge-agent YAML, gateway ingest path, optional orchestrator survival endpoint, coordination/metrics endpoints, local buffer | Deployment-ready and included in full-stack deployment assets | `clear on runtime shape`, `enablement is deployment-specific` |
| `cwi-node-agent-runtime` | Client-side enrollment, activation gating, kill-switch sync, health, task/runtime guardrails | [cli.py](/Users/astrion9/git/cwi-platform/src/node-agent-runtime/node_agent_runtime/cli.py), [Dockerfile](/Users/astrion9/git/cwi-platform/src/node-agent-runtime/Dockerfile) | Always-on health/runtime container plus operator/automation CLI tasks | Client-restricted node/customer runtime | node-agent YAML, coordination enrollment endpoint, orchestrator admin/kill-switch endpoints, local state/keys | Now packaged and deployment-ready | `clear on role`, `phase-gated by environment adoption` |

## Per-Component Notes

## 1. `cwi-etl`

The repo makes this clear:

- [__main__.py](/Users/astrion9/git/cwi-platform/src/etl/cwi_etl/__main__.py) does not start a long-running ETL server,
- [README.md](/Users/astrion9/git/cwi-platform/src/etl/README.md) describes it as deterministic ETL utilities,
- graph and consent flows are implemented as modules rather than one unified daemon.

Interpretation:

- `cwi-etl` is the shared Python ETL layer,
- not the one-and-only deployed ETL runtime by itself.

## 2. `cwi-consent-ingest`

This is the currently explicit always-on ETL deployment role.

Evidence:

- [mcp-core/docker-compose.yml](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/DEPLOY/mcp-core/docker-compose.yml#L206)
- [consent-ingest.md](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/runbooks/consent-ingest.md)

Interpretation:

- if someone asks “what ETL job is definitely running in the core bundle?”, this is the answer.

## 3. Teacher-side graph ETL

This is now real and implemented, but it is not the same thing as `cwi-consent-ingest`.

Evidence:

- [teacher-node README](/Users/astrion9/git/cwi-platform/tools/teacher-node/README.md)
- [graph_cli.py](/Users/astrion9/git/cwi-platform/tools/teacher-node/graph_cli.py)
- [pipeline.py](/Users/astrion9/git/cwi-platform/src/etl/cwi_etl/pipeline.py)

Interpretation:

- teacher-side graph ETL is supposed to run on the teacher/build side,
- not inside the user ask path,
- and not necessarily as a permanently running service unless the team chooses that operating model.
- in the current deployment model, the teacher host keeps a separate `cwi-age-db` alongside the single internal `teacher-node` profile.

## 4. `cwi-connectors`

This is a real runtime.

Evidence:

- [__main__.py](/Users/astrion9/git/cwi-platform/src/connectors/cwi_connectors/__main__.py) starts a health server and can `run_forever`,
- [pyproject.toml](/Users/astrion9/git/cwi-platform/src/connectors/pyproject.toml) defines the package,
- full-stack deployment now includes it.

Interpretation:

- it belongs on an internal integrations host or internal service tier,
- and it is supposed to run continuously when that capability is in scope.

## 5. `cwi-edge-agent`

This is also a real runtime.

Evidence:

- [cli.py](/Users/astrion9/git/cwi-platform/src/edge-agent/edge_agent/cli.py) defines a persistent `run` loop,
- [edge-agent.md](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/runbooks/edge-agent.md) treats it as an operational agent,
- full-stack deployment now includes it.

Interpretation:

- it is supposed to run on client/edge infrastructure,
- continuously, not as an occasional batch script.

## 6. `cwi-node-agent-runtime`

This is a phase-gated but real runtime, not just tests or skeleton code.

Evidence:

- [cli.py](/Users/astrion9/git/cwi-platform/src/node-agent-runtime/node_agent_runtime/cli.py)
- [README.md](/Users/astrion9/git/cwi-platform/src/node-agent-runtime/README.md)
- [node-agent-runtime.md](/Users/astrion9/git/cwi-platform/OPS/cwi-ops/runbooks/node-agent-runtime.md)
- [Dockerfile](/Users/astrion9/git/cwi-platform/src/node-agent-runtime/Dockerfile)

Interpretation:

- it is supposed to run on client-restricted nodes when that decentralized runtime role is active,
- with health/readiness running continuously and activation/enrollment/kill-switch flows handled operationally.

## Where/When Summary

### Always-on internal Python runtime/job

- `cwi-consent-ingest`
- `cwi-connectors` when integrations are enabled

### Always-on client/edge Python runtime

- `cwi-edge-agent`
- `cwi-node-agent-runtime` when node runtime scope is active

### Manual / CI / teacher-side Python tooling

- `teacher-node`
- teacher graph tooling via `cwi-etl` wrappers

### Shared Python package layer

- `cwi-etl`

## Bottom Line

Yes, we are now clear on the technical shape.

We are not yet fully clear on one policy question:

- which optional Python runtimes are supposed to be enabled in the target environment now, versus merely kept deployment-ready.

The main operational ambiguity is not “what does the code do?” anymore.
It is:

- which environments should run which Python runtimes,
- and whether teacher-side graph ETL should remain operator/CI-driven or become a scheduled production workflow.
