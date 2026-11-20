# CWI Edge Agent

Lightweight edge agent skeleton with config validation and CLI.

## Quick start
```bash
cwi-edge-agent validate-config --config ./configs/edge-agent.example.yaml
cwi-edge-agent run --config ./configs/edge-agent.example.yaml --max-batch 25
cwi-edge-agent import-file --input ./data/telemetry.csv --mapping ./configs/telemetry_tag_map.example.yaml --tenant-id tenant-1
cwi-edge-agent import-file --input ./data/telemetry.csv --mapping ./configs/telemetry_tag_map.example.yaml --tenant-id tenant-1 --buffer ./buffer.jsonl
cwi-edge-agent opcua-poll --endpoint opc.tcp://localhost:4840 --mapping ./configs/telemetry_tag_map.example.yaml --tenant-id tenant-1
```

## Config
- `gateway_url`: CWI gateway URL (https://...)
- `tenant_id`: tenant identifier
- `poll_interval_seconds`: polling cadence (seconds)
- `buffer_path`: local buffer path (file or directory)
- `buffer_max_items`: optional cap on queued items (drop policy applies)
- `buffer_max_bytes`: optional cap on buffer file size
- `buffer_drop_policy`: `drop_oldest` (default) or `drop_newest`
- `ingest_timeout_seconds`: per-request timeout when sending telemetry.
- `identity.*` or `CWI_EDGE_AGENT_IDENTITY_*`: identity login/refresh settings for runtime token acquisition
  - the identity must belong to the same tenant the agent submits as (`tenant_id`)
  - use a dedicated service account rather than a human or global super-admin account
- `mtls.cert_path` / `mtls.key_path`: optional mTLS client cert/key for telemetry ingest
- `mtls.revoked_serials_path`: optional revoked serials list (fail-closed)
- `mtls.ca_bundle_path`: optional CA bundle for server verification
- `mtls.crl_url`: optional CRL URL to check revocations
- `mtls.crl_cache_path`: optional CRL cache location
- `mtls.crl_refresh_seconds`: optional CRL refresh interval
- `mtls.pinned_fingerprints`: optional SHA256 cert fingerprints to pin (hex or `sha256:`)
- `mtls.verify_hostname`: toggle hostname verification (default true)
- `mtls.reload_interval_seconds` / `mtls.reload_on_sighup`: audit cert reload checks
- `survival.*`: optional survival signal emission to `/api/survival/signal`
- `degradation.*`: thresholds for buffer/network/disk degradation computation
- `otel.*`: OpenTelemetry exporter settings (traces + metrics)
  - `otel.headers` can be a map or comma-separated `key=value` string.
- `metrics.*`: optional link metrics collection/export to coordination layer
  - `metrics.collector`: `simulated`, `system` (install `pip install .[metrics]`), or `none`
  - `metrics.endpoint` / `metrics.endpoints`: coordination-layer base URL(s)

## Telemetry import
The importer expects CSV/JSON rows with a timestamp column and signal fields.
Use `configs/telemetry_tag_map.example.yaml` to map source column names to tags/units.
An idempotency key is generated per telemetry window and included in metadata.

## OPC UA collector
Add `opcua_node` fields in the tag map to enable OPC UA polling. The command uses
the optional `asyncua` dependency (install separately).

## Buffering
Provide `--buffer` to append payloads to a jsonl buffer file. The buffer
tracks an internal cursor in a sibling `.cursor` file and can apply
bounded persistence when configured.

## Degradation signals
The agent computes resource/network degradation signals (buffer pressure,
disk pressure, circuit breaker state) and can emit them to the orchestrator
`/api/survival/signal` endpoint when thresholds are exceeded.

## OpenTelemetry
Enable `otel.enabled` and set an OTLP endpoint to export traces/metrics.
Install with `pip install .[otel]`. The agent emits ingest spans, counters,
latency histograms, and degradation severity metrics.

## Resilience
The run command sends buffered payloads with exponential backoff and a simple
circuit breaker when the gateway is unreachable. The retry loop honors the
buffer drop policy when storage limits are reached.

## Certificate revocation
Provide a revoked serial list file (one serial per line). The client will fail-closed
if the configured mTLS certificate serial appears in the list. Optionally
configure a CRL URL with local caching for revocation checks. Certificates are
reloaded on each request, so file replacement supports rotation. Reload checks
emit audit events on interval or SIGHUP (if enabled).

## Packaging
Build a container image from `Dockerfile`:
```bash
docker build -t cwi-edge-agent:local .
```

## Smoke test
Use `tools/edge-agent-smoke/edge-agent-smoke.ps1` to start the stack and send a
sample telemetry file through the buffer.
