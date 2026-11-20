# CWI Connectors Runbook

## Local run
1. Ensure Python 3.10+ is available.
2. Install dependencies with `pip install -e src/connectors`.
3. Configure `configs/connectors.example.yaml`.

## Docker
```bash
docker build -f src/connectors/docker/Dockerfile -t cwi-connectors .
docker run --rm cwi-connectors
```

## Network hardening
- External connector URLs require https and non-private hosts by default.
- Dev overrides:
  - `CWI_CONNECTORS_ALLOW_INSECURE_HTTP=true`
  - `CWI_CONNECTORS_ALLOW_PRIVATE_URLS=true`
- HTTP tuning:
  - `CWI_CONNECTORS_HTTP_TIMEOUT_SECONDS`
  - `CWI_CONNECTORS_MAX_RESPONSE_BYTES`
  - `CWI_CONNECTORS_USER_AGENT`

## Troubleshooting
- Verify state store file permissions and path.
- Validate connector credentials and access scopes.
- Use `/admin/status` (requires `admin_token`) to inspect last run status + queue depth.
- Trigger `/admin/run` for manual retries during incidents.

## Queued delivery
- Provenance + reconciliation submissions are queued locally if dependencies are down.
- Restore upstream dependencies and allow the queue to drain.

## Retention
- Retention enforcement deletes expired documents via the vector ingest delete endpoint.
- If deletes fail, entries remain in the retention state store for retry.

## Rollback
- Redeploy previous container image tag and restore state store backup.
