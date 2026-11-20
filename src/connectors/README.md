# CWI Connectors

Connector runtime framework for external sources.

## Runtime config
See `configs/connectors.example.yaml` for a base configuration. Secrets can be provided
via `env://` or `file://` references (for example, `env://CONNECTOR_API_KEY`).
When using connector identity bindings, enable JWT signature validation by
setting `orchestrator_jwks_url` (or `orchestrator_jwks_path`) plus optional
`orchestrator_jwt_issuer`/`orchestrator_jwt_audience`. If JWKS is unavailable,
set `orchestrator_allow_insecure_jwt: true` for local/dev only.

## Network hardening
- External connector URLs must be https and non-private by default.
- Override for dev only:
  - `CWI_CONNECTORS_ALLOW_INSECURE_HTTP=true`
  - `CWI_CONNECTORS_ALLOW_PRIVATE_URLS=true`
- HTTP controls:
  - `CWI_CONNECTORS_HTTP_TIMEOUT_SECONDS` (default 20)
  - `CWI_CONNECTORS_MAX_RESPONSE_BYTES` (default 10485760)
  - `CWI_CONNECTORS_USER_AGENT` (optional override)

## Admin/monitoring
Run the service with the HTTP health server enabled (default `:8080`).

- `/health` returns 200 when the process is running
- `/ready` returns 200 when dependencies (state store) are reachable
- `/metrics` exposes Prometheus counters
- `/admin/status` returns last-run status + queue depth (requires `admin_token`)
- `/admin/run` triggers a run (POST JSON `{ "connector_id": "..." }`, requires `admin_token`)

## Normalization & vector ingestion
Normalize connector outputs into a `DocumentRecord`, apply redaction + chunking, then ingest
via `VectorIngestClient`. Provenance summaries can be emitted to the orchestrator.
Canonical records are derived per document with source priority + provenance metadata and
submitted to the reconciliation API when configured.

## Connector scaffolds
- Google Drive: `GoogleDriveConnector`
- Outlook: `OutlookConnector`
- Smartsheet: `SmartsheetConnector`
- Structured CSV: `CsvStructuredConnector`
- JSON: `JsonConnector`

See `docs/connector_specs.md` for planned XML/email/doc connectors + privacy requirements.

## Live integration tests (opt-in)
To exercise live API calls, omit `fixture_path` and provide `api_base_url` plus
auth config. Fixtures are opt-in: set `fixture_path` to a file under
`contracts/fixtures` for dev/test runs. Tests are gated by
`CWI_CONNECTORS_LIVE_TESTS=1` and per-connector env vars in
`tests/test_live_connectors.py`.

## OAuth setup (high level)
1. Create an OAuth application with read-only scopes for the connector.
2. Store credentials in a secret manager and expose them to the runtime via
   `env://` or `file://` references.
3. Configure the connector `auth` block with the token URL and scopes.

## Secret rotation
Rotate credentials in the secret store and restart the connector runtime. The
runtime reads secrets at startup to avoid leaking values into logs.

## Queues & retention
- Provenance + reconciliation submissions use a local delivery queue for at-least-once delivery.
- Retention jobs track ingested documents and attempt to delete expired records from the vector service.

## Compatibility and versioning
- Connector plugins must declare `sdk_version` and match `CONNECTOR_SDK_VERSION`.
- Compatibility rules follow `docs/adr/ADR-PLAT-001-contract-versioning.md` and the
  schema matrix in `docs/platform/iteration-04/schema-compatibility-matrix.md`.

## Docker
```bash
docker build -f src/connectors/docker/Dockerfile -t cwi-connectors .
docker run --rm cwi-connectors
```
