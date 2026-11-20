# cwi-ops

This directory contains deployment bundles, environment validation scripts, and ops defaults for the 10H baseline.

## Compose bundles
- `compose/cwi-identity-server`
- `compose/cwi-identity-admin`
- `compose/cwi-identity-admin-ui`
- `compose/cwi-identity-stack`
- `compose/cwi-orchestrator`
- `compose/cwi-coordination-layer`
- `compose/pack-service`
- `compose/vector_shard`
- `compose/gpu_worker`
- `compose/teacher_node`
- `compose/gateway`
- `compose/cwi-glassbox-web-ui`
- `compose/cwi-gpt-web-ui`
- `compose/cwi-homepage-web-ui`
- `compose/full`
- `compose/test`

## Deploy bundles
- `DEPLOY/cloudflared`
- `DEPLOY/gateway`
- `DEPLOY/cwi-gpt-web-ui`
- `DEPLOY/cwi-glassbox-web-ui`
- `DEPLOY/cwi-homepage-web-ui`
- `DEPLOY/cwi-admin-console`
- `DEPLOY/cwi-identity-admin`
- `DEPLOY/connectors`
- `DEPLOY/edge_agent`
- `DEPLOY/node_agent_runtime`
- `DEPLOY/cwi-orchestrator`
- `DEPLOY/mcp-core`
- `DEPLOY/graph_db`
- `DEPLOY/vector_shard`
- `DEPLOY/gpu_worker`
- `DEPLOY/teacher_node`
- `DEPLOY/teacher_minio` (optional adjunct, not part of the default teacher deployment)
- `DEPLOY/cwi-teams-bot`
- `DEPLOY/pack-service`

## Container naming (zones)
Container names follow `{zone}-{stack}-{service}`.

Zones in use:
- `public`: Internet-exposed ingress/reverse-proxy host and public-facing surfaces (gateway + public UIs; upstream services remain internal).
- `internal-01`: Core control-plane/services (routing, orchestration, identity, bots, pack serving).
- `internal-02`: Data/evidence/compute/tooling layers (datastores, vector/graph, model workers, teacher tooling).
- `client-restricted`: Runs only on the client/customer node.

Examples:
- `public-core-gateway`
- `internal-01-core-router`
- `internal-02-teacher-node`
- `internal-02-data-redis`

Notes: Compose service names are unchanged; use them with `docker compose`. Container names are for ops, logging, and monitoring.

## Env var matrix
Authoritative matrix: `OPS/cwi-ops/env/ENV_VAR_MATRIX.json`
Documentation: `OPS/ENV_VAR_MATRIX.md`

## Validation
```
python OPS/cwi-ops/scripts/validate-env.py --role cwi-orchestrator --env-file OPS/cwi-ops/compose/cwi-orchestrator/.env
.\OPS\cwi-ops\scripts\validate-env.ps1 -Role cwi-orchestrator -EnvFile OPS/cwi-ops/compose/cwi-orchestrator/.env
python OPS/cwi-ops/scripts/scan-secrets.py --path OPS/cwi-ops/compose
.\OPS\cwi-ops\scripts\scan-secrets.ps1 -Path OPS/cwi-ops/compose
```
