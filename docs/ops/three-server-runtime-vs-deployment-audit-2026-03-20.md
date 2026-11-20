# Three-Server Runtime vs Deployment Audit

Date: 2026-03-20

Scope assumption: "all 3 servers" refers to the three core hosts:
- `public-gateway-01`
- `internal-mcp-01`
- `internal-teacher-01`

Source: live `docker ps` on each host, compared against the current repo deployment bundles under `OPS/cwi-ops/DEPLOY`.

| App | Server (Where it's running) | Deployment Included |
| --- | --- | --- |
| `cwi-gpt-web-ui` | `public-gateway-01` | Yes |
| `cwi-gateway` | `public-gateway-01` | Yes |
| `cadvisor` | `public-gateway-01` | No |
| `node-exporter` | `public-gateway-01` | No |
| `cwi-orchestrator` | `internal-mcp-01` | Yes |
| `cwi-pack-service` | `internal-mcp-01` | Yes |
| `cwi-router` | `internal-mcp-01` | Yes |
| `cwi-consent-ingest` | `internal-mcp-01` | Yes |
| `cwi-teams-bot` | `internal-mcp-01` | Yes |
| `cwi-coordination-layer` | `internal-mcp-01` | Yes |
| `cwi-identity-server` | `internal-mcp-01` | Yes |
| `cwi-pin-resolver` | `internal-mcp-01` | Yes |
| `cwi-identity-admin` | `internal-mcp-01` | Yes |
| `postgres (coordination)` | `internal-mcp-01` | Yes |
| `postgres (identity)` | `internal-mcp-01` | Yes |
| `postgres (orchestrator)` | `internal-mcp-01` | Yes |
| `redis` | `internal-mcp-01` | Yes |
| `cwi-graph-db` | `internal-mcp-01` | Yes |
| `cadvisor` | `internal-mcp-01` | No |
| `node-exporter` | `internal-mcp-01` | No |
| `cwi-age-db` | `internal-teacher-01` | Yes |
| `teacher-node (internal-02-teacher-node)` | `internal-teacher-01` | Yes |
| `teacher-node (client-restricted-teacher-node)` | `internal-teacher-01` | No |
| `teacher-minio (client-restricted-teacher-minio)` | `internal-teacher-01` | No |
| `cwi-minio` | `internal-teacher-01` | Yes |
| `nextcloud-app` | `internal-teacher-01` | No |
| `nextcloud-db` | `internal-teacher-01` | No |
| `nextcloud-redis` | `internal-teacher-01` | No |
| `cadvisor` | `internal-teacher-01` | No |
| `node-exporter` | `internal-teacher-01` | No |

## Notes

- `cwi-consent-ingest` is deployment-included on `internal-mcp-01`, but it is currently restart-looping.
- `cwi-teams-bot` is deployment-included on `internal-mcp-01`, but it is currently unhealthy.
- `internal-teacher-01` still has runtime drift: one correct `teacher-node`, one legacy extra `teacher-node`, two MinIO containers, and a Nextcloud stack that are not part of the current intended deployment model.
