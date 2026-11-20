# Full-stack local ecosystem

This compose bundle brings up the core CWI ecosystem for local validation:
- gateway, router, orchestrator, coordination layer
- identity server + admin UI
- pack-service + packs
- vector shard + graph DB + mock model worker health
- teams bot + teacher node + retained teacher AGE DB
- connectors + edge agent + node agent runtime
- homepage web UI
- Glassbox web UI
- GPT web UI
- admin console

## Prereqs
- Docker Engine + docker compose v2
- Built images for core services (router/orchestrator/coordination/identity/teacher-node)

## Quick start (PowerShell)
```powershell
Copy-Item .env.template .env
.\start.ps1
```

## Quick start (bash)
```bash
cp .env.template .env
./start.sh
```

## Stop
```powershell
.\stop.ps1
```

```bash
./stop.sh
```

## Local-only ports (override)
To expose internal services on localhost **without** changing the base zone model, use the local override file.

PowerShell:
```powershell
# Uses .env + .env.local for port values
$envFiles = @("--env-file", ".env", "--env-file", ".env.local")
$composeFiles = @("-f", "docker-compose.full.yml", "-f", "docker-compose.local.yml")
docker compose @envFiles @composeFiles up -d --build
```

Bash:
```bash
docker compose --env-file .env --env-file .env.local \
  -f docker-compose.full.yml -f docker-compose.local.yml up -d --build
```

## URLs
- Gateway: http://localhost:18081
- Homepage Web UI: http://localhost:18094
- Glassbox Web UI: http://localhost:18090
- GPT Web UI: http://localhost:18093
- Connectors API: http://localhost:8080
- Node Agent health: http://localhost:8090/health

## Notes
- The base bundle does not publish internal/admin services; use the local override to expose loopback-only ports.
- `NEXT_PUBLIC_*` values are injected at build time for the Glassbox Web UI. Update `.env` and re-run `start.ps1` to rebuild.
- `ADMIN_CONSOLE_*` values are injected at build time for the admin console. Update `.env` and re-run `start.ps1` to rebuild.
- Teams bot runs in unauthenticated mode for local testing (`ALLOW_UNAUTHENTICATED=true`).
- Teacher node uses filesystem-backed artifacts in the full-stack bundle.
- Core graph DB (Apache AGE) is reachable on the `cwi-full` network with DSN from `CWI_GRAPH_DB_DSN`.
- The retained teacher-side AGE DB is also present in the bundle and is exposed in the local override on `127.0.0.1:${CWI_AGE_DB_HOST_PORT:-5455}`.
- `cwi-connectors` uses `compose/full/connectors.yaml`.
- `cwi-edge-agent` uses `compose/full/edge-agent.yaml`.
- `cwi-node-agent-runtime` uses `compose/full/node-agent.yaml` and starts with the health server by default.



## Identity admin decommission (plan)
- `cwi-identity-admin` is legacy UI; `cwi-admin-console` is the target admin surface.
- Keep identity-admin available until admin console parity for tenants/users/roles is verified.
- Cutover steps: update DNS/ingress so the admin domain routes to the admin console, then remove identity-admin from public compose/ingress.
- Rollback steps: re-enable identity-admin in compose/ingress and restore previous DNS routing if parity issues are discovered.
- Full plan and validation checklist: `docs/platform/iteration-10/identity-admin-deprecation.md`.

## Consent ingest
- Drop consent payloads into `compose/full/consent-input/` as JSON or JSONL. The consent-ingest container archives
  processed files and writes consent records into the local SQLite store.
- Adjust schedule with `CWI_CONSENT_INGEST_INTERVAL_SECONDS` in `.env`.
