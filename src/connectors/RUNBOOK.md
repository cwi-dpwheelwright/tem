# CWI Connectors Runbook

## OAuth setup
1. Create a read-only OAuth application for the connector (Drive/Outlook/etc.).
2. Store client secrets in your secret manager.
3. Configure `auth` in `connectors.yaml` using `env://` or `file://` references.

## Scheduling
- `poll_interval_seconds` controls how often connectors run.
- Use `--once` for backfills or manual runs.

## Network hardening
- External connector URLs require https and non-private hosts by default.
- Dev overrides:
  - `CWI_CONNECTORS_ALLOW_INSECURE_HTTP=true`
  - `CWI_CONNECTORS_ALLOW_PRIVATE_URLS=true`
- HTTP tuning:
  - `CWI_CONNECTORS_HTTP_TIMEOUT_SECONDS`
  - `CWI_CONNECTORS_MAX_RESPONSE_BYTES`
  - `CWI_CONNECTORS_USER_AGENT`

## JWT validation (identity bindings)
- Configure `orchestrator_jwks_url` (or `orchestrator_jwks_path`) to enforce JWT signature validation.
- Set `orchestrator_jwt_issuer` and `orchestrator_jwt_audience` if your identity provider requires it.
- Ensure the JWKS endpoint is reachable from the runtime container.
- If JWKS is unavailable in local/dev, set `orchestrator_allow_insecure_jwt: true` and limit usage to non-production.

## Retention
- Set `retention_days` in the connector config if you need to skip stale items.
- Ensure retention policies are aligned with orchestrator/vector shard policies.

## Troubleshooting
- Check `/health` and `/metrics` to confirm the runtime is alive.
- Inspect the state store (`connector_state` table or JSON file) for cursor drift.
- Increase logging locally by running with `PYTHONWARNINGS=default`.
 - Use `/admin/status` (with `admin_token`) to inspect last run status + queue depth.
 - Trigger `/admin/run` for manual reruns during incident response.

## Secret rotation
- Update the secret in the backing store, then restart the container.
- Avoid printing secrets; use `env://` or `file://` references only.

## Queued delivery
- Provenance + reconciliation submissions are queued locally if dependencies are down.
- Clear failures by restoring the upstream service and letting the queue drain.

## Retention
- Retention enforcement deletes expired documents via the vector ingest delete endpoint.
- If deletes fail, entries remain queued in the retention state store for retry.
