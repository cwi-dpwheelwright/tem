# Repo vs Runtime App Audit — 2026-03-20

Prepared for: Demitrius / leadership / platform ops  
Purpose: Audit where repo-backed apps are currently running, compare live runtime to the repo inventory, and identify gaps, drift, and extra workloads.

## Scope

This audit compares:

- apps and deployable services currently present in the repo
- against live containers running on the currently reachable CWI hosts

Audited hosts:

- `public-gateway-01` (`100.124.140.66`)
- `internal-mcp-01` (`100.120.208.51`)
- `internal-teacher-01` (`100.87.205.59`)
- `vector-shard-1` (`100.115.118.16`)
- `vector-shard-2` (`100.91.142.103`)
- `gpu-worker-01` (`100.87.13.127`)
- `gpu-worker-02` (`100.90.161.26`)

Repo inventory sources used:

- [created-apps-deployment-inventory-2026-03-19.md](/Users/astrion9/git/cwi-platform/docs/ops/created-apps-deployment-inventory-2026-03-19.md)
- [component-role-inventory.md](/Users/astrion9/git/cwi-platform/docs/architecture/component-role-inventory.md)
- `src/backend/services/`
- `src/frontend/web-ui/`
- `src/connectors/`
- `src/edge-agent/`
- `src/etl/`
- `src/node-agent-runtime/`
- `tools/teacher-node/`
- `OPS/cwi-ops/DEPLOY/`

Live runtime evidence used:

- direct `ssh root@<host> docker ps -a --format ...`
- targeted `docker inspect`
- targeted service health checks where useful

## Executive Summary

The current environment is only partially aligned with the repo’s deployable app inventory.

### Running and repo-aligned

- `cwi-gateway`
- `cwi-gpt-web-ui`
- `cwi-router`
- `cwi-orchestrator`
- `cwi-coordination-layer`
- `cwi-identity-server`
- `cwi-pack-service`
- `cwi-teams-bot`
- `cwi-etl` / `cwi-consent-ingest`
- `teacher-node`
- `teacher-minio`
- `graphdb` / `cwi-graph-db`
- vector shards
- GPU/model-worker stack
- `cwi-identity-admin`

### Present but unhealthy / drifted

- `cwi-consent-ingest` is deployed but crash-looping
- `cwi-teams-bot` is running but unhealthy
- `teacher-node` is duplicated on the teacher host
- MinIO is duplicated on the teacher host
- a second Apache AGE instance is running on the teacher host in addition to the MCP graph DB

### In repo and deployable, but not currently observed running on audited hosts

- `cwi-glassbox-web-ui`
- `cwi-homepage-web-ui`
- `cwi-admin-console`
- `cwi-connectors`
- `cwi-edge-agent`
- `cwi-node-agent-runtime`

### Extra / non-aligned live workloads

- `Nextcloud` stack on `internal-teacher-01`
- `cAdvisor` and `node-exporter` on all audited hosts
- exited / leftover model/vector workloads on some hosts
- `cwi-pin-resolver` is live, but there is no current source tree for it in this repo

## Repo Apps and Where They Are Running

| Repo app / service | Repo evidence | Live host(s) | Live container(s) | Current status | Audit note |
| --- | --- | --- | --- | --- | --- |
| `cwi-gateway` | `OPS/cwi-ops/DEPLOY/gateway/docker-compose.yml` | `public-gateway-01` | `public-core-gateway` | running, healthy | aligned |
| `cwi-gpt-web-ui` | `src/frontend/web-ui/cwi-gpt-web-ui`; `DEPLOY/cwi-gpt-web-ui` | `public-gateway-01` | `public-ui-gpt-web-ui` | running | aligned |
| `cwi-router` | `src/backend/services/cwi-router` | `internal-mcp-01` | `internal-01-core-router` | running | aligned |
| `cwi-orchestrator` | `src/backend/services/cwi-orchestrator` | `internal-mcp-01` | `internal-01-core-orchestrator` | running | aligned |
| `cwi-coordination-layer` | `src/backend/services/cwi-coordination-layer` | `internal-mcp-01` | `internal-01-core-coordination-layer` | running, healthy | aligned |
| `cwi-identity-server` | `src/backend/services/cwi-identity-server` | `internal-mcp-01` | `internal-01-identity-server` | running | aligned |
| `cwi-pack-service` | `src/backend/services/cwi-pack-service` | `internal-mcp-01` | `internal-01-core-pack-service` | running | aligned |
| `cwi-teams-bot` | `src/backend/services/cwi-teams-bot` | `internal-mcp-01` | `internal-01-bot-teams` | running, unhealthy | deployed but degraded |
| `cwi-etl` | `src/etl`; `src/etl/docker/Dockerfile` | `internal-mcp-01` | `internal-01-etl-consent-ingest` | restarting | deployed but failing |
| `cwi-consent-ingest` | `DEPLOY/mcp-core/docker-compose.yml` | `internal-mcp-01` | `internal-01-etl-consent-ingest` | restarting | same ETL job as above |
| `teacher-node` | `tools/teacher-node`; `DEPLOY/teacher_node` | `internal-teacher-01` | `client-restricted-teacher-node`, `internal-02-teacher-node` | both running | duplicated |
| `teacher-minio` | teacher adjunct in teacher bundle | `internal-teacher-01` | `client-restricted-teacher-minio`, `cwi-minio` | both running | duplicated |
| `graphdb` / `cwi-graph-db` | `DEPLOY/mcp-core/docker-compose.yml` | `internal-mcp-01` | `internal-02-data-graph-db` | running, healthy | aligned with repo deployment model |
| vector shard / Qdrant | `DEPLOY/vector_shard/docker-compose.yml` | `vector-shard-1`, `vector-shard-2` | `qdrant` | running on both shards | aligned |
| GPU/model-worker stack | `DEPLOY/gpu_worker/docker-compose.yml` | `gpu-worker-01`, `gpu-worker-02` | multiple `internal-02-model-worker*`, health proxies, capability endpoints | running | live repo-managed infra not reflected in simplified stack doc |
| `cwi-identity-admin` | `src/frontend/web-ui/cwi-identity-admin`; `DEPLOY/cwi-identity-admin` | `internal-mcp-01` | `internal-01-identity-admin` | running | live, but not called out in simplified stack doc |

## Repo Apps Not Observed Running on Audited Hosts

These apps exist in the repo and have deployment coverage, but were not observed in `docker ps -a` on the audited host set.

| Repo app | Repo evidence | Live status in audited environment | Audit note |
| --- | --- | --- | --- |
| `cwi-glassbox-web-ui` | `src/frontend/web-ui/cwi-glassbox-web-ui`; `DEPLOY/cwi-glassbox-web-ui` | not observed running | deployable, not enabled here |
| `cwi-homepage-web-ui` | `src/frontend/web-ui/cwi-homepage-web-ui`; `DEPLOY/cwi-homepage-web-ui` | not observed running | deployable, not enabled here |
| `cwi-admin-console` | `src/frontend/web-ui/cwi-admin-console`; `DEPLOY/cwi-admin-console` | not observed running | deployable, not enabled here |
| `cwi-connectors` | `src/connectors`; `DEPLOY/connectors` | not observed running | optional / environment-specific runtime not enabled on audited hosts |
| `cwi-edge-agent` | `src/edge-agent`; `DEPLOY/edge_agent` | not observed running | client/edge runtime not present on audited server set |
| `cwi-node-agent-runtime` | `src/node-agent-runtime`; `DEPLOY/node_agent_runtime` | not observed running | phase-gated runtime not present on audited server set |

## Repo Items That Are Not Deployable App Runtimes

These are in the repo, but they are not expected to appear as standalone server containers in this audit.

| Repo item | Classification | Audit note |
| --- | --- | --- |
| graph tooling wrappers such as `cwi_entity_extractor.py`, `cwi_graph_rollup.py`, `cwi_graph_crosswalk.py` | teacher tooling entrypoints | expected to run through `teacher-node`, not as separate containers |
| repo-native graph ETL modules under `src/etl/cwi_etl/` | library/module layer | expected to be consumed by `teacher-node` or ETL jobs |
| `cwi-user-web` | placeholder only | not deployable and not observed |
| mobile apps | non-container client apps | out of scope for server-container audit |

## Live Runtime Items With Repo Drift or Gaps

### 1. `cwi-consent-ingest` is deployed but failing

Host:

- `internal-mcp-01`

Container:

- `internal-01-etl-consent-ingest`

State:

- `Restarting (1)`

Observed failure:

- `PermissionError: [Errno 13] Permission denied: '/srv/cwi'`
- `FileNotFoundError: [Errno 2] No such file or directory: '/srv/cwi/consent-input'`

Interpretation:

- the ETL job exists in runtime
- but it is currently not functioning

### 2. `cwi-teams-bot` is live but unhealthy

Host:

- `internal-mcp-01`

Container:

- `internal-01-bot-teams`

State:

- `Up 8 days (unhealthy)`

Interpretation:

- the app is deployed and running
- but it should be treated as degraded until the unhealthy condition is resolved

### 3. Teacher runtime duplication exists

Host:

- `internal-teacher-01`

Observed duplicate stacks:

- `client-restricted-teacher-node`
- `internal-02-teacher-node`
- `client-restricted-teacher-minio`
- `cwi-minio`

Interpretation:

- there are two teacher-node deployments and two MinIO deployments on the same host
- this is runtime drift and should be reconciled

### 4. Dual graph DB runtime exists

Observed:

- `internal-02-data-graph-db` on `internal-mcp-01`
- `cwi-age-db` on `internal-teacher-01`

Interpretation:

- the repo deployment model treats graph DB as part of MCP core
- the teacher host is also running a separate AGE instance
- this needs explicit architectural clarification

### 5. `cwi-pin-resolver` is live, but not source-backed in the current repo

Host:

- `internal-mcp-01`

Container:

- `internal-01-core-pin-resolver`

Image:

- `local/cwi-pin-resolver:phase5`

Repo status:

- referenced in docs and deployment manifests
- no current service source tree found in this repo

Interpretation:

- this is a live runtime dependency
- but not a normal source-backed app in the current codebase snapshot

## Live Runtime Items Not Part of the Current Repo App Inventory

These were observed live but are not part of the current repo’s main app inventory.

| Live runtime | Host(s) | Note |
| --- | --- | --- |
| `nextcloud-app`, `nextcloud-db-1`, `nextcloud-redis-1` | `internal-teacher-01` | colocated workload; not part of current CWI app stack docs |
| `cadvisor` | all audited hosts | observability workload |
| `node-exporter` | all audited hosts | observability workload |
| `dcgm-exporter` | both GPU hosts | GPU observability workload |
| `vllm-14b` | `gpu-worker-01` | exited leftover workload |
| `ollama` exited on vector shard | `vector-shard-1` | stray / old runtime (`ollama`, exited) |
| `cwi-qdrant` created but not running | `vector-shard-1` | leftover container |

## Important Documentation Mismatches Surfaced

### The simplified current stack doc is incomplete relative to live runtime

The current simplified stack file does not capture:

- the two GPU/model-worker hosts
- the live `identity-admin` container on MCP
- the duplicate teacher stacks
- the extra AGE instance on the teacher host
- the standard observability sidecars

### Public serving layout differs from local-compose assumptions

The public gateway host is actively serving:

- `cwi-gateway` on `80/443`
- `cwi-gpt-web-ui` on `18093`

That differs from some local/full-compose defaults and is worth treating as environment-specific runtime shape.

## Operational Conclusions

1. The core MCP runtime is mostly present and live on `internal-mcp-01`.
2. The ETL job is deployed, but currently failing.
3. The teacher/graph side is running, but the host has clear duplication and drift.
4. Several repo apps are deployment-ready but not enabled in the currently audited environment.
5. The simplified stack documentation no longer fully matches the live runtime.
6. The GPU/model-worker runtime is live on two additional hosts and should be included in any true “current runtime” inventory.

## Recommended Next Steps

1. Update the stack docs to include:
   - the GPU/model-worker hosts
   - `identity-admin`
   - the current teacher-host drift state
2. Fix `internal-01-etl-consent-ingest` by correcting the consent input path/mount/permissions on `/srv/cwi`.
3. Reconcile the duplicate teacher-node and MinIO stacks on `internal-teacher-01`.
4. Decide whether the teacher-host `cwi-age-db` is:
   - intentional staging infrastructure,
   - a legacy leftover,
   - or an active part of the production graph path.
5. Decide which currently deployable but inactive repo apps should be enabled in this environment:
   - `cwi-glassbox-web-ui`
   - `cwi-homepage-web-ui`
   - `cwi-admin-console`
   - `cwi-connectors`
   - `cwi-edge-agent`
   - `cwi-node-agent-runtime`
