# DEPLOY: mcp-core

Minimal bundle for the MCP internal host:
- cwi-router
- cwi-orchestrator
- cwi-coordination-layer
- cwi-identity-server (+ identity-postgres)
- cwi-pack-service
- cwi-pin-resolver
- cwi-teams-bot
- cwi-consent-ingest (ETL)
- cwi-edge-agent
- cwi-node-agent-runtime
- redis
- graph-db (Apache AGE)

## Setup
1. Copy `.env.template` to `.env` and set real values.
2. Validate envs (run each role check):
   - `python OPS/cwi-ops/scripts/validate-env.py --role cwi-orchestrator --env-file OPS/cwi-ops/DEPLOY/mcp-core/.env --allow-extra`
   - `python OPS/cwi-ops/scripts/validate-env.py --role cwi-coordination-layer --env-file OPS/cwi-ops/DEPLOY/mcp-core/.env --allow-extra`
   - `python OPS/cwi-ops/scripts/validate-env.py --role cwi-router --env-file OPS/cwi-ops/DEPLOY/mcp-core/.env --allow-extra`
   - `python OPS/cwi-ops/scripts/validate-env.py --role cwi-identity-server --env-file OPS/cwi-ops/DEPLOY/mcp-core/.env --allow-extra`
   - `python OPS/cwi-ops/scripts/validate-env.py --role graph_db --env-file OPS/cwi-ops/DEPLOY/mcp-core/.env --allow-extra`
   - `python OPS/cwi-ops/scripts/validate-env.py --role teams_bot --env-file OPS/cwi-ops/DEPLOY/mcp-core/.env --allow-extra`
   - `python OPS/cwi-ops/scripts/validate-env.py --role consent_ingest --env-file OPS/cwi-ops/DEPLOY/mcp-core/.env --allow-extra`
   - `python OPS/cwi-ops/scripts/validate-env.py --role cwi-edge-agent --env-file OPS/cwi-ops/DEPLOY/mcp-core/.env --allow-extra`
   - `python OPS/cwi-ops/scripts/validate-env.py --role cwi-node-agent-runtime --env-file OPS/cwi-ops/DEPLOY/mcp-core/.env --allow-extra`
3. Start:
   - `docker compose up -d`

## ETL image
Build and push the ETL image before deployment:
```
docker build -f src/etl/docker/Dockerfile -t ${REGISTRY}/cwi-etl:${CWI_ETL_TAG} src/etl
docker push ${REGISTRY}/cwi-etl:${CWI_ETL_TAG}
```

## Additional Python runtime images
Build and push these images before deployment when the MCP bundle includes them:
```
docker build -f src/backend/services/cwi-pin-resolver/Dockerfile -t ${REGISTRY}/cwi-pin-resolver:phase5 src/backend/services/cwi-pin-resolver
docker push ${REGISTRY}/cwi-pin-resolver:phase5

docker build -f src/edge-agent/Dockerfile -t ${REGISTRY}/cwi-edge-agent:${CWI_EDGE_AGENT_TAG} src/edge-agent
docker push ${REGISTRY}/cwi-edge-agent:${CWI_EDGE_AGENT_TAG}

docker build -f src/node-agent-runtime/Dockerfile -t ${REGISTRY}/cwi-node-agent-runtime:${CWI_NODE_AGENT_RUNTIME_TAG} src/node-agent-runtime
docker push ${REGISTRY}/cwi-node-agent-runtime:${CWI_NODE_AGENT_RUNTIME_TAG}
```

## Notes
- `REDIS_URL` uses a scheme: `redis://redis:6379` (required by the teams bot client).
- `PACKS_DIR` must exist on the host and contain the packs to serve.
- Pin-resolver host directories must exist and match the values in `.env`:
  - `PINS_DIR`
  - `KEYS_DIR`
  - `LEDGER_DIR`
  - `PACKS_DIR`
- `PIN_RESOLVER_IMAGE` should point at the built resolver image, for example:
  - `${REGISTRY}/cwi-pin-resolver:phase5`
- Consent ingest host directories must exist and be writable:
  - `CWI_CONSENT_INGEST_INPUT_HOST_DIR`
  - `CWI_CONSENT_INGEST_ARCHIVE_HOST_DIR`
  - `CWI_CONSENT_INGEST_ERROR_HOST_DIR`
  - `CWI_CONSENT_DB_HOST_DIR`
- Edge/node runtime state directories are host bind mounts by default and must exist and be writable before first start:
  - `CWI_EDGE_AGENT_STATE_DIR` (default `/srv/cwi/edge-agent-state`)
  - `CWI_NODE_AGENT_RUNTIME_STATE_DIR` (default `/srv/cwi/node-agent-runtime-state`)
- Recommended ownership for the default host state dirs:
  - edge-agent: UID `10001`
  - node-agent-runtime: UID `10002`
- `cwi-edge-agent` authenticates via identity login credentials in `CWI_EDGE_AGENT_IDENTITY_*`
- When using identity login, the configured identity must belong to the same tenant as `edge-agent.yaml` (`tenant-1` by default). Do not use a cross-tenant super-admin identity for telemetry ingest.
- MCP-local agent configs are bundled here:
  - `edge-agent.yaml`
  - `node-agent.yaml`
- `ORCHESTRATOR_DB_CONNECTION` and `COORDINATION_DB_CONNECTION` are external DBs unless you add another Postgres container.
- `orchestrator-postgres` is published for Grafana/PostgreSQL dashboards via:
  - `ORCHESTRATOR_DB_BIND_IP` (default `127.0.0.1`)
  - `ORCHESTRATOR_DB_HOST_PORT` (default `15433`)
- For Grafana on a different host, set `ORCHESTRATOR_DB_BIND_IP` in the deployed `.env` to the MCP host's private interface address that Grafana can reach. Avoid `0.0.0.0` unless host firewall/VPN policy limits access to approved operators and Grafana.
- Keep Postgres port changes in this repo bundle. Inline edits under `/opt/cwi/DEPLOY/mcp-core` or old `/opt/cwi/build-contexts/*` copies will be overwritten by repo-driven deploys.
- Scope-dependent or public-surface apps are deployed via adjunct bundles, not this core bundle:
  - `OPS/cwi-ops/DEPLOY/gateway`
  - `OPS/cwi-ops/DEPLOY/cwi-gpt-web-ui`
  - `OPS/cwi-ops/DEPLOY/cwi-glassbox-web-ui`
  - `OPS/cwi-ops/DEPLOY/cwi-homepage-web-ui`
  - `OPS/cwi-ops/DEPLOY/cwi-admin-console`
  - `OPS/cwi-ops/DEPLOY/cwi-identity-admin`
  - `OPS/cwi-ops/DEPLOY/connectors`
