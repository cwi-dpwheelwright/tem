# DEPLOY: node_agent_runtime

## Purpose
Compose bundle for the client-side node agent runtime.

## Files
- docker-compose.yml
- .env.template
- node-agent.yaml

## Setup
1. Copy `.env.template` to `.env` and set `CWI_HOME_NODE_WORK_TOKEN_SECRET` to match `HOME_NODE_WORK_TOKEN_SECRET` on the live orchestrator.
2. Adjust `node-agent.yaml` for your coordinator, gateway, tenant, owner user, and QR metadata settings.
3. Start services:
   `docker compose up -d`

## Home node target
The current home node deployment target is `root@100.122.244.48`.

From the repo root, deploy it with:
`powershell OPS/cwi-ops/DEPLOY/deploy.ps1 -InventoryPath OPS/cwi-ops/DEPLOY/deploy.inventory.home-node.json`

The inventory builds `local/cwi-node-agent-runtime:home-node-20260422` on the home node, copies this bundle to `/opt/cwi/DEPLOY/node_agent_runtime`, and starts the health, QR, bootstrap, and control-session services.

## Validate
- Config:
  `docker compose exec cwi-node-agent-runtime cwi-node-agent validate-config --config /etc/cwi/node-agent.yaml`
- Enrollment:
  `docker compose exec cwi-node-agent-runtime cwi-node-agent enroll --config /etc/cwi/node-agent.yaml`
- Execution gate:
  `docker compose exec cwi-node-agent-runtime cwi-node-agent check-execution --config /etc/cwi/node-agent.yaml`
- Health:
  `curl http://localhost:${NODE_AGENT_RUNTIME_HTTP_PORT:-8090}/health`
- Readiness:
  `curl http://localhost:${NODE_AGENT_RUNTIME_HTTP_PORT:-8090}/ready`
- QR service health:
  `curl http://localhost:${NODE_AGENT_QR_HTTP_PORT:-8088}/health`
- QR payload:
  `curl http://localhost:${NODE_AGENT_QR_HTTP_PORT:-8088}/payload.json`
- QR SVG:
  `curl http://localhost:${NODE_AGENT_QR_HTTP_PORT:-8088}/qr.svg > node-link.svg`

## Notes
- The QR service is intended for administrators already on the VPN/Tailscale network.
- `network_mode: host` exposes the QR service directly on the node's VPN-facing interfaces.
- If `identity.node_id: auto` is enabled, mount the host `/etc/machine-id` into the container so the runtime can derive the machine-bound node ID.
