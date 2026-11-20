# Three-Server Pre-Deploy Checklist

**Date:** 2026-03-20  
**Scope:** `public-gateway-01`, `internal-mcp-01`, `internal-teacher-01`  
**Purpose:** Final pre-deploy checklist for the planned full redeploy using the current repo deployment model.

## Deployment assumptions

- Host cleanup now runs before deploy when enabled.
- Cleanup exclusions apply across all hosts and currently preserve only:
  - `*cadvisor*`
  - `*node-exporter*`
  - `nextcloud*`
- Everything else is removed before deployment:
  - non-excluded containers
  - non-excluded images
  - non-excluded named volumes

## Shared checklist

- Docker is installed and available on all three hosts.
- SSH access is working to all three hosts.
- `cadvisor` and `node-exporter` will remain in place after cleanup.
- `nextcloud*` on the teacher host will remain in place after cleanup.
- All non-excluded CWI app containers will be removed and recreated during the deployment.
- Confirm all required `.env` files are prepared before rollout.
- Confirm required images are available locally on the hosts or reachable via registry.

## public-gateway-01

### Intended roles

- `gateway`
- `cwi-gpt-web-ui`

### Live host checks

- Docker present: `29.2.1`
- `/opt/cwi` exists
- `/opt/cwi/DEPLOY` exists
- `/srv/cwi` does not exist
- Current public ports in use:
  - `80`
  - `443`
  - `8082`
  - `9100`

### Pre-deploy status

- OK: host layout is sufficient for gateway/UI deployment.
- OK: no ` /srv/cwi` dependency is required for the current public deployment model.
- Expected cleanup result:
  - preserved: `cadvisor`, `node-exporter`
  - removed/recreated: `public-core-gateway`, `public-ui-gpt-web-ui`

### Deployment-day checks

- Ensure `OPS/cwi-ops/DEPLOY/gateway/.env` is ready.
- Ensure `OPS/cwi-ops/DEPLOY/cwi-gpt-web-ui/.env` is ready.
- Confirm DNS/TLS settings in the gateway env/Caddy config are correct.

## internal-mcp-01

### Intended roles

- `mcp-core`
- `cwi-identity-admin`
- MCP-host Python runtimes now included in `mcp-core`:
  - `cwi-consent-ingest`
  - `cwi-edge-agent`
  - `cwi-node-agent-runtime`

### Live host checks

- Docker present: `29.2.1`
- `/opt/cwi` exists
- `/opt/cwi/DEPLOY` exists
- `/opt/cwi/DEPLOY/mcp-core` exists
- Required host data paths already exist:
  - `/srv/cwi`
  - `/srv/cwi/consent-input`
  - `/srv/cwi/consent-archive`
  - `/srv/cwi/consent-error`
  - `/srv/cwi/consent-store`
  - `/srv/cwi/packs`
  - `/srv/cwi/pins`
- Current MCP ports in use:
  - `15432`
  - `18083`
  - `18084`
  - `18085`
  - `18086`
  - `8082`
  - `9100`

### Pre-deploy status

- OK: required consent-ingest host directories already exist.
- OK: required packs/pins host directories already exist.
- ACTION REQUIRED: create MCP-host runtime state dirs before redeploy, because the MCP bundle bind-mounts them by default:
  - `/srv/cwi/edge-agent-state`
  - `/srv/cwi/node-agent-runtime-state`
- ACTION REQUIRED: make those state dirs writable by the runtime container users:
  - edge-agent UID `10001`
  - node-agent-runtime UID `10002`
- OK: redeploy should fix the current `cwi-consent-ingest` restart loop because the repo bundle now uses corrected in-container paths.
- Expected cleanup result:
  - preserved: `cadvisor`, `node-exporter`
  - removed/recreated: the full MCP stack, including `internal-01-etl-consent-ingest`, `internal-01-edge-agent`, and `internal-01-node-agent-runtime`

### Deployment-day checks

- Ensure `OPS/cwi-ops/DEPLOY/mcp-core/.env` is ready.
- Ensure `OPS/cwi-ops/DEPLOY/cwi-identity-admin/.env` is ready.
- Confirm these host-path envs are set correctly if overriding defaults:
  - `CWI_CONSENT_INGEST_INPUT_HOST_DIR`
  - `CWI_CONSENT_INGEST_ARCHIVE_HOST_DIR`
  - `CWI_CONSENT_INGEST_ERROR_HOST_DIR`
  - `CWI_CONSENT_DB_HOST_DIR`
- Ensure MCP Python runtime envs are set:
  - `CWI_EDGE_AGENT_IDENTITY_URL`
  - `CWI_EDGE_AGENT_IDENTITY_TENANT_ID`
  - `CWI_EDGE_AGENT_IDENTITY_USERNAME`
  - `CWI_EDGE_AGENT_IDENTITY_PASSWORD` or `CWI_EDGE_AGENT_IDENTITY_PASSWORD_PATH`
  - `NODE_AGENT_RUNTIME_HTTP_PORT`
- Set or create the MCP-host runtime state dirs:
  - `CWI_EDGE_AGENT_STATE_DIR` (default `/srv/cwi/edge-agent-state`)
  - `CWI_NODE_AGENT_RUNTIME_STATE_DIR` (default `/srv/cwi/node-agent-runtime-state`)
- Confirm graph DB envs point at the intended MCP graph data path.
- `cwi-identity-admin` should be included in the MCP rollout plan because it is part of the current runtime placement and live host state.

## internal-teacher-01

### Intended roles

- `teacher_node`
- `graph_db`

### Decision locked

- Keep the current internal teacher-node deployment as part of the target deployment model.
- Canonical runtime to preserve through redeploy: `internal-02-teacher-node`
- Legacy duplicate to remove and not recreate: `client-restricted-teacher-node`

### Live host checks

- Docker present: `29.2.1`
- `/opt/cwi` exists
- `/opt/cwi/DEPLOY` exists
- `/opt/cwi/DEPLOY/teacher_node` exists
- `/srv/cwi` exists
- `/srv/cwi/contracts` exists
- `/srv/cwi/teacher-node-artifacts` exists
  - currently a symlink to `/srv/cwi/Knowledge-Folders`
- Current teacher ports in use:
  - `8084`
  - `9000`
  - `9001`
  - `5455`
  - `8082`
  - `9100`

### Important live-vs-bundle path differences

- Live `teacher-node` artifacts:
  - host path: `/srv/cwi/teacher-node-artifacts`
  - this matches the current bundle default and is OK to keep
- Live `cwi-age-db` data path:
  - current live host path: `/srv/cwi/graph-db/postgres`
  - current bundle requires `CWI_GRAPH_DB_DATA_DIR`
  - `/srv/cwi/graph-db/postgres` already exists and should be preserved
- Live `cwi-age-db` also mounts:
  - `/srv/cwi/graph-work/age` read-only
  - the standalone `DEPLOY/graph_db` bundle now supports preserving this live parity mount

### Pre-deploy status

- OK: `teacher-node` artifact and contracts paths exist.
- OK: the canonical `internal-02-teacher-node` is the teacher-node instance we are keeping in the deployment model.
- OK: teacher-node should stay filesystem-backed through the redeploy.
- OK: the duplicate legacy teacher containers should be removed by cleanup and replaced by the current deployment model.
- ACTION REQUIRED: ensure the teacher graph DB env points at the existing live data directory.
- ACTION REQUIRED: set the graph-work mount env if you want to preserve the current teacher graph DB workflow shape.
- Expected cleanup result:
  - preserved: `cadvisor`, `node-exporter`, `nextcloud-app`, `nextcloud-db-1`, `nextcloud-redis-1`
  - removed/recreated: `internal-02-teacher-node`, `cwi-age-db`
  - removed/not recreated unless explicitly kept elsewhere: `client-restricted-teacher-node`, `client-restricted-teacher-minio`, `cwi-minio`

### Deployment-day checks

- Ensure `OPS/cwi-ops/DEPLOY/teacher_node/.env` is ready.
- Ensure `OPS/cwi-ops/DEPLOY/graph_db/.env` is ready.
- To preserve the current teacher graph DB data, set:
  - `CWI_GRAPH_DB_DATA_DIR=/srv/cwi/graph-db/postgres`
- To preserve the current teacher graph DB workflow shape, set:
  - `CWI_GRAPH_WORK_AGE_HOST_DIR=/srv/cwi/graph-work/age`
- For the current filesystem-backed teacher-node target, confirm these envs are set in `teacher_node/.env`:
  - `TEACHER_NODE_ARTIFACT_BACKEND=filesystem`
  - `TEACHER_NODE_ARTIFACT_HOST_PATH=/srv/cwi/teacher-node-artifacts`
  - `TEACHER_NODE_ARTIFACT_ROOT=/data/teacher-node`
  - `ORCHESTRATOR_URL=http://100.120.208.51:18085`

## Recommended deployment order

1. `internal-mcp-01`
   - deploy `mcp-core`
   - deploy `cwi-identity-admin`
2. `internal-teacher-01`
   - deploy `teacher_node`
   - deploy `graph_db`
3. `public-gateway-01`
   - deploy `gateway`
   - deploy `cwi-gpt-web-ui`

## Key risks to avoid

- Do not redeploy teacher `cwi-age-db` without setting `CWI_GRAPH_DB_DATA_DIR` and, if needed for parity, `CWI_GRAPH_WORK_AGE_HOST_DIR`.
- Do not forget that cleanup will remove all non-excluded CWI containers, images, and named volumes before the new deployment starts.
- Do not assume the current live ETL crash loop is a code bug; it should be resolved by redeploying the corrected MCP bundle.
