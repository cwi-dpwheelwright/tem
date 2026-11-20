# Running Container Audit — 2026-03-20

Prepared for: CWI leadership / Demitrius / platform ops  
Audited from: `/Users/astrion9/git/cwi-platform`  
Audit method date: 2026-03-20

## Purpose

Record the currently running container inventory across the known CWI servers and compare that live state to the documented stack.

## Important Audit Constraint

Direct `docker ps` over SSH was not possible from this workstation because all five hosts required a Tailscale SSH approval check instead of key-based noninteractive access.

The audit was therefore performed using:

- live network reachability checks,
- service health endpoint checks,
- live `cAdvisor` container metadata exposed on port `8082`,
- existing documented server inventory in [current-application-stack.md](/Users/astrion9/git/cwi-platform/docs/ops/current-application-stack.md).

This means:

- the container inventory below is live and host-derived,
- but it is derived from `cAdvisor` rather than `docker ps`,
- so restart counts and exact Docker status strings were not available from this workstation.

## Known Server Inventory Used

Source: [current-application-stack.md](/Users/astrion9/git/cwi-platform/docs/ops/current-application-stack.md)

| Server | VPN IP | Documented purpose |
| --- | --- | --- |
| `public-gateway-01` | `100.124.140.66` | `cwi-gateway` + `cwi-gpt-web-ui` |
| `internal-teacher-01` | `100.87.205.59` | `teacher-node` |
| `internal-mcp-01` | `100.120.208.51` | router/orchestrator/core services |
| `vector-shard-1` | `100.115.118.16` | Qdrant |
| `vector-shard-2` | `100.91.142.103` | Qdrant |

## Executive Summary

The live container audit shows that the stack is partially aligned with the current documentation, but there are several important mismatches:

- `public-gateway-01` is running the expected gateway and GPT UI.
- `internal-mcp-01` is running the expected core MCP services, but also has `identity-admin` running even though it is not listed in the current stack doc.
- `internal-mcp-01` is also running `internal-01-etl-consent-ingest`, but that container is currently in a restart loop.
- `internal-teacher-01` is running substantially more than the current stack doc states:
  - two teacher-node containers,
  - two MinIO containers,
  - an Apache AGE container,
  - a full Nextcloud stack.
- both vector shards are running Qdrant as expected.
- all audited hosts also expose `cadvisor` and `node-exporter`, which are not called out in the current simplified stack file.
- `cwi-teams-bot` is running but its health endpoint currently reports degraded status.
- the public gateway host responds on `80` and `443`, not on `18081`.
- `internal-mcp-01` is answering on `18083`, `18084`, and `18085`, but `18082` was not externally reachable during the audit.

## Live Container Inventory By Host

Container inventory below was extracted from live `cAdvisor` metadata on each host.

### `public-gateway-01` (`100.124.140.66`)

Observed running containers:

- `public-core-gateway` -> `caddy:2`
- `public-ui-gpt-web-ui` -> `local/cwi-gpt-web-ui:10h`
- `cadvisor` -> `gcr.io/cadvisor/cadvisor:latest`
- `node-exporter` -> `prom/node-exporter:latest`

Observed network behavior:

- `http://100.124.140.66/` returned `200`
- `http://100.124.140.66/healthz` returned `200`
- `http://100.124.140.66/api/health` returned `200`
- `http://100.124.140.66:18093/` returned `200`
- port `80` open
- port `443` open
- port `18081` closed
- port `11434` closed
- port `11435` closed

Important note:

- the public host is behaving like the gateway is serving the primary web surface on `80`, while the GPT UI is also directly reachable on `18093`
- the public `/api/health` payload reported `llm: down`

### `internal-mcp-01` (`100.120.208.51`)

Observed running containers:

- `internal-01-core-router` -> `local/cwi-router:10h`
- `internal-01-core-orchestrator` -> `local/cwi-orchestrator:10h`
- `internal-01-core-coordination-layer` -> `local/cwi-coordination-layer:10h`
- `internal-01-core-pack-service` -> `local/cwi-pack-service:10h`
- `internal-01-core-pin-resolver` -> `local/cwi-pin-resolver:phase5`
- `internal-01-identity-server` -> `local/cwi-identity-server:10h`
- `internal-01-bot-teams` -> `local/cwi-teams-bot:10h`
- `internal-01-etl-consent-ingest` -> `local/cwi-etl:10h`
- `internal-02-data-redis` -> `redis:7.2-alpine`
- `internal-02-data-graph-db` -> `apache/age:latest`
- `internal-02-identity-postgres` -> `postgres:16-alpine`
- `internal-02-orchestrator-postgres` -> `postgres:16-alpine`
- `internal-02-coordination-postgres` -> `postgres:16-alpine`
- `internal-01-identity-admin` -> `local/cwi-identity-admin:10h`
- `cadvisor` -> `gcr.io/cadvisor/cadvisor:latest`
- `node-exporter` -> `prom/node-exporter:latest`

Observed network behavior:

- `http://100.120.208.51:8085/jwks` returned `200`
- `http://100.120.208.51:18083/packs` returned `200`
- `http://100.120.208.51:18084/api/health` returned `200`
- `http://100.120.208.51:18085/api/health` returned `200`
- `http://100.120.208.51:3978/health` returned `503`
- port `3978` open
- port `8085` open
- port `18083` open
- port `18084` open
- port `18085` open
- port `13978` closed
- port `18082` closed
- port `5432` closed

Health detail:

- router health reported `{"orchestrator":"ok","redis":"ok"}`
- orchestrator health reported `database`, `provenance`, `packs`, `vector_shard_1`, `vector_shard_2`, and `model` all `ok`
- teams bot health reported `503` because its `gateway` dependency was `down`, while `redis` was `ok`
- `internal-01-etl-consent-ingest` is present but restarting with `ExitCode 1`
- recent ETL logs show:
  - `PermissionError: [Errno 13] Permission denied: '/srv/cwi'`
  - `FileNotFoundError: [Errno 2] No such file or directory: '/srv/cwi/consent-input'`

Important notes:

- `identity-admin` is running on the MCP host even though it is not listed in [current-application-stack.md](/Users/astrion9/git/cwi-platform/docs/ops/current-application-stack.md)
- the live teams-bot port is `3978`, not `13978`
- port `8082` on this host is `cAdvisor`, not pack-service
- the coordination-layer container is present, but the expected host port was not externally reachable during this audit

### `internal-teacher-01` (`100.87.205.59`)

Observed running containers:

- `client-restricted-teacher-node` -> `local/cwi-teacher-node:10h`
- `internal-02-teacher-node` -> `local/cwi-teacher-node:10h`
- `client-restricted-teacher-minio` -> `minio/minio:RELEASE.2024-05-27T19-17-46Z`
- `cwi-minio` -> `minio/minio:latest`
- `cwi-age-db` -> `apache/age:release_PG16_1.6.0`
- `nextcloud-app` -> `nextcloud:apache`
- `nextcloud-db-1` -> `postgres:16`
- `nextcloud-redis-1` -> `redis:7`
- `cadvisor` -> `gcr.io/cadvisor/cadvisor:latest`
- `node-exporter` -> `prom/node-exporter:latest`

Observed network behavior:

- `http://100.87.205.59:9001/` returned `200`
- `http://100.87.205.59:9000/minio/health/live` returned `200`
- port `9000` open
- port `9001` open
- port `8080` closed
- port `8081` closed

Important notes:

- this host is running materially more than the simplified stack doc describes
- there are two teacher-node containers and two MinIO containers present
- a separate `cwi-age-db` is present here in addition to the graph DB running on `internal-mcp-01`
- a full Nextcloud stack is also present
- teacher-node itself was not externally reachable on `8080` during this audit

### `vector-shard-1` (`100.115.118.16`)

Observed running containers:

- `qdrant` -> `qdrant/qdrant:latest`
- `cadvisor` -> `gcr.io/cadvisor/cadvisor:latest`
- `node-exporter` -> `prom/node-exporter:latest`

Observed network behavior:

- `http://100.115.118.16:6333/healthz` returned `200`
- `http://100.115.118.16:6333/collections` returned `200`
- collection list included `cwi_kb_primary`
- port `6333` open
- port `6334` open

### `vector-shard-2` (`100.91.142.103`)

Observed running containers:

- `qdrant` -> `qdrant/qdrant:latest`
- `cadvisor` -> `gcr.io/cadvisor/cadvisor:latest`
- `node-exporter` -> `prom/node-exporter:latest`

Observed network behavior:

- `http://100.91.142.103:6333/healthz` returned `200`
- `http://100.91.142.103:6333/collections` returned `200`
- collection list included `cwi_kb_primary`
- port `6333` open
- port `6334` open

## Alignment vs Current Stack Doc

Compared to [current-application-stack.md](/Users/astrion9/git/cwi-platform/docs/ops/current-application-stack.md):

### Aligned

- `public-gateway-01` does have `cwi-gateway` and `cwi-gpt-web-ui`
- `internal-mcp-01` does have router, orchestrator, pack-service, identity-server, teams-bot, Redis, and graph DB
- both vector shard hosts do have Qdrant

### Not fully reflected in docs

- all hosts also have `cadvisor` and `node-exporter`
- `internal-mcp-01` also has `identity-admin`
- `internal-teacher-01` also has:
  - Apache AGE on teacher host
  - duplicate teacher-node/MinIO stacks
  - Nextcloud app/database/Redis

### Behavior mismatches

- public gateway traffic appears to be served on `80/443`, not `18081`
- teams bot is healthy enough to answer, but currently degraded because its gateway dependency is failing
- coordination-layer container exists, but its expected host port was not reachable externally

## Operational Risks / Follow-ups

1. The teacher host should be reviewed for stack drift.
   Two teacher-node containers, two MinIO containers, a separate AGE instance, and a Nextcloud stack suggest the host is doing more than the current documentation says.

2. The MCP host should be reviewed for undocumented runtime scope.
   `identity-admin` is live there even though it is not listed in the simplified stack inventory.

3. The teams-bot dependency chain should be checked.
   It is running, but health is degraded because the gateway check is failing.

4. The ETL consent-ingest container should be fixed.
   It is deployed, but crash-looping because it cannot create or access `/srv/cwi/consent-input`.

5. The coordination-layer exposure should be confirmed.
   The container is present in live inventory, but the expected host port was not externally reachable during the audit.

6. The docs should be updated to distinguish:
   - business/runtime containers,
   - observability containers,
   - auxiliary containers,
   - and non-CWI colocated workloads.

7. SSH-based audit access should be restored for future audits.
   `docker ps`, `docker compose ps`, restart counts, logs, and container health states still need a real SSH-capable audit path.

## Recommended Next Step

Use this audit as the baseline live inventory, then do a second-pass SSH audit once Tailscale SSH approval or deploy keys are available. That second pass should collect:

- `docker ps --format ...`
- `docker compose ps`
- restart counts
- failing container logs
- exact compose project roots on each host
- confirmation of which containers are intentionally colocated vs legacy leftovers
