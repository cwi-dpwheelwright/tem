# Runbook: CWI Edge Agent

## Purpose
Troubleshoot edge-agent telemetry ingestion, buffering, and survival signals.

## Golden fixtures
- `src/edge-agent/configs/telemetry_sample.csv`
- `src/edge-agent/configs/telemetry_sample.expected.json`
- `src/edge-agent/configs/telemetry_tag_map.example.yaml`

## Quick checks
1. Validate config:
   `cwi-edge-agent validate-config --config /etc/cwi/edge-agent.yaml`
2. Confirm buffer growth:
   check `<buffer_path>` and `<buffer_path>.cursor`
3. Verify ingest path:
   `curl -H "Authorization: Bearer <jwt>" <gateway>/api/ingest/telemetry`

## Failure modes + actions
- **Buffer grows, cursor stuck at 0**
  - Gateway unreachable or TLS failure. Check edge-agent logs for `Ingest failed`.
  - Validate `gateway_url` and `ingest_timeout_seconds`.
  - Confirm mTLS files are present and not revoked.
- **TLS pinning failures**
  - Ensure `mtls.pinned_fingerprints` matches the server cert SHA256.
  - Rotate pins alongside cert rotation.
- **Survival signals missing**
  - Ensure `survival.enabled` is true.
  - Confirm `/api/survival/signal` is reachable from edge agent.
- **OTel exporter silent**
  - Ensure `otel.enabled` is true and `otlp_endpoint` is reachable.
  - Check exporter headers for auth.

## Audit events
When `mtls.reload_interval_seconds` or `mtls.reload_on_sighup` is set, cert
reload checks emit JSON audit events to stdout:
```
{"event_type":"cert_reload","observed_utc":"...","details":{"changed_paths":["/etc/cwi/edge-agent.crt"]}}
```

## Escalation
If ingestion failures persist after TLS + network checks, capture:
- edge-agent logs
- buffer file + cursor
- gateway health output
and escalate to on-call.
